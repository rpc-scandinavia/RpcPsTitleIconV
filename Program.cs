using System.Text;

// Filter for CUPS-PDF.
// This filter modifies the PostScript DSC header before it reaches cups-pdf.
// It removes unwanted parentheses from metadata fields such as Title and For.
// It also converts UTF-8 encoded PostScript octal sequences to the Latin-1 representation expected
// by Ghostscript for PDF metadata.
// The filter only modifies the PostScript header, the document body is passed through unchanged.
//
// This filter either read PostScript from a file or from standard input.
// It outputs the PostScript on standard output, and it is thus important that nothing else is written there.
// All logging are written to standard error.
//
// That is the reason this is a program, and not a DotNet file-based program, because DotNet craps all ower standard
// output, with build information, telemetry nag information, and first time run information. 

String logPrefix = "pstitleiconv: ";

try {
    // Validate.
    if ((args.Length < 5) || (args.Length > 6)) {
		throw new ArgumentException($"The number of arguments should be 5 or 6. Expecting JobId, User, Title, Copies, Options and optional InputFileName.");
    }

    // Get the arguments.
    String job = args[0];
    String user = args[1];
    String title = args[2];
    String copies = args[3];
    String options = args[4];
    FileInfo file = (args.Length >= 6) ? new FileInfo(args[5]) : null;

	// Log.
	Console.Error.WriteLine($"{logPrefix}Arguments: Job: '{job}', User: '{user}', Title: '{title}', Copies: '{copies}', Options: '{options}', File: '{file}'.");

	// The header is either from the header file, or standard header.
	using (Stream input = ((file != null) && (file.Exists == true)) ? File.OpenRead(file.FullName) : Console.OpenStandardInput()) {
		using Stream output = Console.OpenStandardOutput();
		Int32 count = ProcessPostScript(input, output, title);

		// Log.
		Console.Error.WriteLine($"{logPrefix}Successfully processed {count} bytes of PostScript.");
	}

	Environment.ExitCode = 0;
} catch (Exception exception) {
	// Log error.
	Console.Error.WriteLine($"{logPrefix}{exception.Message}");
	Environment.ExitCode = 1;
}

Int32 ProcessPostScript(Stream input, Stream output, String title) {
	const Int32 maxCommentsSize = (100 * 1024);				// Don't try to read more header than 100 kilobytes.
	Byte[] endComments = Encoding.ASCII.GetBytes("%%EndComments");
	MemoryStream header = new MemoryStream();

	Byte[] buffer = new Byte[1024];
	Boolean headerFinished = false;
	Int32 count = 0;

	// Read into the header memory stream, until the end-of-comments is read or the maximum amount is reached.
	while ((headerFinished == false) && (header.Length < maxCommentsSize)) {
		// Read the next byte.
		Int32 inputByte = (Byte)input.ReadByte();
		if (inputByte == -1) {
			break;
		}
		header.WriteByte((Byte)inputByte);
		count++;

		// Check if the header has been read.
		if (header.Length >= endComments.Length) {
			header.Position = header.Length - endComments.Length;
			Boolean match = true;
			for (Int32 headerIndex = 0; headerIndex < endComments.Length; headerIndex++) {
				if (endComments[headerIndex] != (Byte)header.ReadByte()) {
					match = false;
					break;
				}
			}

			headerFinished = match;
			header.Seek(0, SeekOrigin.End);
		}
	}

	// DEBUG: Write header.
	//header.Seek(0, SeekOrigin.Begin);
	//Console.Error.WriteLine("== header ================================================================");
	//Console.Error.WriteLine(Encoding.UTF8.GetString(header.ToArray()));
	//Console.Error.WriteLine("==========================================================================");

	// Modify the header.
	header = ProcessPostScriptHeader(header, "Title", ((String value) => Recode(TrimParentheses(value))));
	header = ProcessPostScriptHeader(header, "For", TrimParentheses);
	
	// Copy the header.
	header.Seek(0, SeekOrigin.Begin);
	header.CopyTo(output);

	// Copy everything after the header completely unchanged.
	while (true) {
		Int32 readCount = input.Read(buffer, 0, buffer.Length);
		if (readCount <= 0) {
			break;
		}

		output.Write(buffer, 0, readCount);
		count += readCount;
	}

	output.Flush();

	return count;
} // ProcessPostScript

/// <summary>
/// Convert UTF-8 encoded PostScript octal sequences to the Latin-1 representation expected by Ghostscript for PDF
/// metadata.
/// Example:
///		UTF-8 string: "ÆØÅ æøå"
///		UTF-8 octal sequence: "\303\206\303\230\303\205 \303\246\303\270\303\245"
///		Latin-1 octal sequence: "\306\330\305 \346\370\345".
/// </summary>
String Recode(String value) {
	if (value == null) {
		return value;
	}

	Encoding inputEncoding = Encoding.UTF8;
	Encoding outputEncoding = Encoding.Latin1;

	List<Byte> inputBytes = new List<Byte>();

	for (Int32 index = 0; index < value.Length; index++) {
		if ((value[index] == '\\') &&
			(index + 3 < value.Length) &&
			Char.IsDigit(value[index + 1]) &&
			Char.IsDigit(value[index + 2]) &&
			Char.IsDigit(value[index + 3])) {

			inputBytes.Add(Convert.ToByte(value.Substring(index + 1, 3), 8));
			index += 3;
		} else {
			inputBytes.AddRange(Encoding.ASCII.GetBytes([value[index]]));
		}
	}

	String unicodeValue = inputEncoding.GetString(inputBytes.ToArray());
	Byte[] outputBytes = outputEncoding.GetBytes(unicodeValue);

	StringBuilder result = new StringBuilder();

	foreach (Byte outputByte in outputBytes) {
		if ((outputByte >= 0x20) && (outputByte <= 0x7E)) {
			result.Append((Char)outputByte);
		} else {
			result.Append('\\');
			result.Append(Convert.ToString(outputByte, 8).PadLeft(3, '0'));
		}
	}

	return result.ToString();
} // Recode

/// <summary>
/// Remove starting and ending parentheses from the value, but only if the value both start and end with parentheses.
/// </summary>
String TrimParentheses(String value) {
	if ((value != null) &&
		(value.StartsWith('(') == true) &&
		(value.EndsWith(')') == true)) {
		return value.Substring(1, value.Length - 2);
	} else {
		return value;
	}
} // TrimParentheses

// ==header==================================================================
// %!PS-Adobe-3.0
// %%Invocation: gs -q -dNOPAUSE -dBATCH -dSAFER -dNOMEDIAATTRS -sstdout=? -sDEVICE=ps2write -dShowAcroForm -sOUTPUTFILE=? -dLanguageLevel=2 -r300 -dCompressFonts=false -dNoT3CCITT -dNOINTERPOLATE ? ? -f ?
// %%HiResBoundingBox: 0 0 596.00 842.00
// %%Creator: GPL Ghostscript 10080 (ps2write)
// %%LanguageLevel: 2
// %%CreationDate: D:20261002224553+02\'00\'
// %%For: (rpc@rpc-scandinavia.dk)
// %%Title: (Dette er en (st\\303\\270rre) pr\\303\\270ve)
// %RBINumCopies: 1
// %%Pages: (atend)
// %%BoundingBox: (atend)
// %%EndComments
// ==========================================================================
MemoryStream ProcessPostScriptHeader(MemoryStream header, String name, Func<String, String> processValue) {
	// Validate.
	if ((header == null) ||
		(String.IsNullOrWhiteSpace(name) == true) ||
		(processValue == null)) {
		return header;
	}

	Byte[] spaceByte =  Encoding.ASCII.GetBytes(" ");
	Byte[] newlineByte =  Encoding.ASCII.GetBytes("\n");
	Byte[] nameBytes = Encoding.ASCII.GetBytes($"%%{name}:");
	Int64 nameBegin = header.IndexOf(nameBytes);
	if (nameBegin > -1) {
		// Replace existing header value.
		Int64 valueBegin = nameBegin + nameBytes.Length;
		Int64 valueEnd = header.IndexOf(newlineByte, valueBegin);
		if (valueEnd > -1) {
			// Get header value.
			Int64 valueLength = ((valueEnd + newlineByte.Length) - valueBegin);
			Byte[] valueBytes = new Byte[valueLength];
			header.Seek(valueBegin, SeekOrigin.Begin);
			header.Read(valueBytes, 0, valueBytes.Length);

			// Process header value.
			String value = Encoding.ASCII.GetString(valueBytes).Trim(' ', '\r', '\n');
			value = processValue(value);

			// Write pre.
			MemoryStream temp = new MemoryStream();
			header.Seek(0, SeekOrigin.Begin);
			header.CopyTo(temp, nameBegin, 4096);

			// Write name and value.
			temp.Write(nameBytes);
			temp.Write(spaceByte);
			temp.Write(Encoding.ASCII.GetBytes(value));
			temp.Write(newlineByte);

			// Write post.
			header.Seek(valueEnd + newlineByte.Length, SeekOrigin.Begin);
			header.CopyTo(temp);

			// Replace the header content with the temp content.
			return temp;
		}

		// Do nothing. This header is corrupt.
	} else {
		// Insert new header value just before the end of the header.
		Byte[] endCommentsBytes = Encoding.ASCII.GetBytes("%%EndComments");
		Int64 endCommentsBegin = header.IndexOf(endCommentsBytes);
		if (endCommentsBegin > -1) {
			// Write pre.
			MemoryStream temp = new MemoryStream();
			header.Seek(0, SeekOrigin.Begin);
			header.CopyTo(temp, endCommentsBegin, 4096);

			// Write name and value.
			temp.Write(nameBytes);
			temp.Write(spaceByte);
			temp.Write(Encoding.ASCII.GetBytes(processValue(String.Empty)));
			temp.Write(newlineByte);

			// Write post.
			header.CopyTo(temp);

			// Replace the header content with the temp content.
			return temp;
		}

		// Do nothing. This header is corrupt.
	}

	return header;
} // ProcessPostScriptHeader



public static class MemoryStreamExtensions {

	extension (MemoryStream stream) {

		/// <summary>
		/// Searches the stream from the start position, for a sequence of bytes matching the find bytes.
		/// </summary>
		/// <param name="findBytes">The byte sequence to find.</param>
		/// <param name="startPosition">The start position in the stream.</param>
		/// <returns>The start index of the find bytes in the stream, or -1.</returns>
		public Int64 IndexOf(Byte[] findBytes, Int64 startPosition = 0) {
			Int64 originalPosition = stream.Position;
			Int64 findPosition = startPosition;
			while (findPosition + findBytes.Length <= stream.Length) {
				stream.Seek(findPosition, SeekOrigin.Begin);
				Boolean match = true;
				for (Int32 searchIndex = 0; searchIndex < findBytes.Length; searchIndex++) {
					if (findBytes[searchIndex] != (Byte)stream.ReadByte()) {
						match = false;
						break;
					}
				}

				if (match == true) {
					// Match found.
					stream.Position = originalPosition;
					return findPosition;
				}

				// Iterate.
				findPosition++;
			}

			// No match.
			stream.Position = originalPosition;
			return -1;
		} // IndexOf

		/// <summary>
		/// Reads the bytes from the current stream and writes them to another stream. Both streams positions are
		/// advanced by the number of bytes copied.
		/// </summary>
		/// <param name="destination">The stream to which the contents of the current stream will be copied.</param>
		/// <param name="maximumCount">The maximum number of bytes that will be copied.</param>
		/// <param name="bufferSize">The size of the buffer. This value must be greater than zero. The default size is 81920.</param>
		/// <returns>The number of bytes that were copied.</returns>
		public Int64 CopyTo(Stream destination, Int64 maximumCount, Int32 bufferSize) {
			Int64 count = 0;
			Byte[] buffer = new Byte[bufferSize];

			while (true) {
				if (bufferSize > (maximumCount - count)) {
					bufferSize = (Int32)(maximumCount - count);
				}

				Int32 readCount = stream.Read(buffer, 0, bufferSize);
				if (readCount <= 0) {
					break;
				}

				destination.Write(buffer, 0, readCount);
				count += readCount;
			}

			destination.Flush();

			return count;
		} // CopyTo

	} // MemoryStream

} // MemoryStreamExtensions
