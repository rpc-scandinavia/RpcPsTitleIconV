## RPC PostScript Title Converter

This is a C# implementatin of the **CUPS-PDF** filter `pstitleiconv`.

Compile into self-contained stand-alone program:

```bash
dotnet  publish  -r linux-x64  --self-contained true
```

### Backstory

I was creating a SystemD extension containing CUPS-PDF for my KDE Linux installation.
But the CUPS-PDF package missed the filter, and thus the **Title** properties were messed up, when using Scandinavian characters.
