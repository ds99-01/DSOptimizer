# Komponenty stron trzecich

## Hanken Grotesk (czcionka)

- Pliki: `src/DSOptimizer.App/Assets/Fonts/HankenGrotesk-{Regular,Medium,SemiBold,Bold}.ttf`, osadzone w `DSOptimizer.exe` jako zasób WPF.
- Źródło: https://github.com/marcologous/hanken-grotesk
- Licencja: SIL Open Font License 1.1 (pełny tekst poniżej oraz w `src/DSOptimizer.App/Assets/Fonts/OFL.txt`).
- Czcionka nie jest modyfikowana ani sprzedawana osobno.

```text
Copyright 2021 The Hanken Grotesk Project Authors (https://github.com/marcologous/hanken-grotesk)


This Font Software is licensed under the SIL Open Font License, Version 1.1.
This license is copied below, and is also available with a FAQ at:
http://scripts.sil.org/OFL


-----------------------------------------------------------
SIL OPEN FONT LICENSE Version 1.1 - 26 February 2007
-----------------------------------------------------------

PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded, 
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.

DEFINITIONS
"Font Software" refers to the set of files released by the Copyright
Holder(s) under this license and clearly marked as such. This may
include source files, build scripts and documentation.

"Reserved Font Name" refers to any names specified as such after the
copyright statement(s).

"Original Version" refers to the collection of Font Software components as
distributed by the Copyright Holder(s).

"Modified Version" refers to any derivative made by adding to, deleting,
or substituting -- in part or in whole -- any of the components of the
Original Version, by changing formats or by porting the Font Software to a
new environment.

"Author" refers to any designer, engineer, programmer, technical
writer or other person who contributed to the Font Software.

PERMISSION & CONDITIONS
Permission is hereby granted, free of charge, to any person obtaining
a copy of the Font Software, to use, study, copy, merge, embed, modify,
redistribute, and sell modified and unmodified copies of the Font
Software, subject to the following conditions:

1) Neither the Font Software nor any of its individual components,
in Original or Modified Versions, may be sold by itself.

2) Original or Modified Versions of the Font Software may be bundled,
redistributed and/or sold with any software, provided that each copy
contains the above copyright notice and this license. These can be
included either as stand-alone text files, human-readable headers or
in the appropriate machine-readable metadata fields within text or
binary files as long as those fields can be easily viewed by the user.

3) No Modified Version of the Font Software may use the Reserved Font
Name(s) unless explicit written permission is granted by the corresponding
Copyright Holder. This restriction only applies to the primary font name as
presented to the users.

4) The name(s) of the Copyright Holder(s) or the Author(s) of the Font
Software shall not be used to promote, endorse or advertise any
Modified Version, except to acknowledge the contribution(s) of the
Copyright Holder(s) and the Author(s) or with their explicit written
permission.

5) The Font Software, modified or unmodified, in part or in whole,
must be distributed entirely under this license, and must not be
distributed under any other license. The requirement for fonts to
remain under this license does not apply to any document created
using the Font Software.

TERMINATION
This license becomes null and void if any of the above conditions are
not met.

DISCLAIMER
THE FONT SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO ANY WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT
OF COPYRIGHT, PATENT, TRADEMARK, OR OTHER RIGHT. IN NO EVENT SHALL THE
COPYRIGHT HOLDER BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
INCLUDING ANY GENERAL, SPECIAL, INDIRECT, INCIDENTAL, OR CONSEQUENTIAL
DAMAGES, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
FROM, OUT OF THE USE OR INABILITY TO USE THE FONT SOFTWARE OR FROM
OTHER DEALINGS IN THE FONT SOFTWARE.
```

## WPF-UI (Wpf.Ui.dll, Wpf.Ui.Abstractions.dll)

- Pliki: `Wpf.Ui.dll`, `Wpf.Ui.Abstractions.dll` (paczki NuGet `WPF-UI` i `WPF-UI.Abstractions` 4.3.0), obok `DSOptimizer.exe`.
- Źródło: https://github.com/lepoco/wpfui (autor: lepo.co).
- Licencja: MIT (`LICENSE.md` w paczce; nuspec: `license type="expression"` MIT). Sprawdzono: paczka NuGet w lokalnej pamięci podręcznej (`~/.nuget/packages`), nuspec i pliki licencji sprawdzone 2026-10-20.
- Wpf.Ui.dll zawiera osadzone komponenty innych autorów, wymienione w `ThirdPartyNotices.txt` paczki. Poniżej ich noty (wszystkie MIT): VirtualizingWrapPanel, fluentui-system-icons (czcionki ikon `FluentSystemIcons-Regular/Filled` osadzone w Wpf.Ui.dll), dotnet/wpf, microsoft-ui-xaml.
- Czcionka „Segoe Fluent Icons” (Microsoft) NIE jest osadzona w Wpf.Ui.dll ani w naszym programie – dll tylko odwołuje się do niej po nazwie, a Windows 11 dostarcza ją sam (w Wpf.Ui.dll nie znaleziono pliku tej czcionki). Licencja Segoe nie zezwala na jej dystrybucję i dlatego jej nie dołączamy.

```text
MIT License

Copyright (c) 2021-2025 Leszek Pomianowski and WPF UI Contributors. https://lepo.co/

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Składniki osadzone w Wpf.Ui.dll:

```text
MIT License (sbaeumlisberger/VirtualizingWrapPanel 2.0.6, https://github.com/sbaeumlisberger/VirtualizingWrapPanel)

Copyright (c) 2019 S. Bäumlisberger

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

```text
MIT License (microsoft/fluentui-system-icons 1.1.242, https://github.com/microsoft/fluentui-system-icons)

Copyright (c) 2020 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

```text
The MIT License (MIT) (dotnet/wpf, https://github.com/dotnet/wpf)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

```text
MIT License (microsoft/microsoft-ui-xaml, https://github.com/microsoft/microsoft-ui-xaml)

Copyright (c) Microsoft Corporation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## CommunityToolkit.Mvvm (CommunityToolkit.Mvvm.dll)

- Plik: `CommunityToolkit.Mvvm.dll` (paczka NuGet 8.4.2).
- Źródło: https://github.com/CommunityToolkit/dotnet (.NET Foundation i współautorzy).
- Licencja: MIT (`License.md` w paczce). Sprawdzono: paczka NuGet w lokalnej pamięci podręcznej (`~/.nuget/packages`), nuspec i pliki licencji sprawdzone 2026-10-20.
- Paczka podaje w `ThirdPartyNotices.txt` m.in. inspirację API kodem lbugnion/mvvmlight (MIT, Copyright (c) 2009 - 2018 Laurent Bugnion) oraz helpery generatorów z Sergio0694/ComputeSharp (MIT, Copyright (c) 2021 Sergio Pedri); ich teksty licencji są takie same jak poniżej.

```text
# .NET Community Toolkit

Copyright © .NET Foundation and Contributors

All rights reserved.

MIT License (MIT)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Newtonsoft.Json (Newtonsoft.Json.dll)

- Plik: `Newtonsoft.Json.dll` (paczka NuGet 13.0.4).
- Źródło: https://github.com/JamesNK/Newtonsoft.Json (James Newton-King).
- Licencja: MIT (`LICENSE.md` w paczce). Sprawdzono: paczka NuGet w lokalnej pamięci podręcznej (`~/.nuget/packages`), nuspec i pliki licencji sprawdzone 2026-10-20.

```text
The MIT License (MIT)

Copyright (c) 2007 James Newton-King

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Microsoft.Bcl.AsyncInterfaces (Microsoft.Bcl.AsyncInterfaces.dll)

- Plik: `Microsoft.Bcl.AsyncInterfaces.dll` (paczka NuGet 10.0.1, wydawca Microsoft, zależność pośrednia).
- Źródło: https://github.com/dotnet/dotnet
- Licencja: MIT (nuspec: `license type="expression"` MIT; paczka nie zawiera osobnego pliku licencji, więc poniżej standardowy tekst MIT z prawem autorskim z nuspec). Sprawdzono: paczka NuGet w lokalnej pamięci podręcznej (`~/.nuget/packages`), nuspec i pliki licencji sprawdzone 2026-10-20.

```text
MIT License

Copyright © Microsoft Corporation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## System.Buffers (System.Buffers.dll)

- Plik: `System.Buffers.dll` (paczka NuGet 4.6.1, wydawca Microsoft, zależność pośrednia).
- Źródło: https://github.com/dotnet/maintenance-packages
- Licencja: MIT (nuspec: `license type="expression"` MIT; paczka nie zawiera osobnego pliku licencji, więc poniżej standardowy tekst MIT z prawem autorskim z nuspec). Sprawdzono: paczka NuGet w lokalnej pamięci podręcznej (`~/.nuget/packages`), nuspec i pliki licencji sprawdzone 2026-10-20.

```text
MIT License

Copyright © Microsoft Corporation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## System.Memory (System.Memory.dll)

- Plik: `System.Memory.dll` (paczka NuGet 4.6.3, wydawca Microsoft, zależność pośrednia).
- Źródło: https://github.com/dotnet/maintenance-packages
- Licencja: MIT (nuspec: `license type="expression"` MIT; paczka nie zawiera osobnego pliku licencji, więc poniżej standardowy tekst MIT z prawem autorskim z nuspec). Sprawdzono: paczka NuGet w lokalnej pamięci podręcznej (`~/.nuget/packages`), nuspec i pliki licencji sprawdzone 2026-10-20.

```text
MIT License

Copyright © Microsoft Corporation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## System.Numerics.Vectors (System.Numerics.Vectors.dll)

- Plik: `System.Numerics.Vectors.dll` (paczka NuGet 4.6.1, wydawca Microsoft, zależność pośrednia).
- Źródło: https://github.com/dotnet/maintenance-packages
- Licencja: MIT (nuspec: `license type="expression"` MIT; paczka nie zawiera osobnego pliku licencji, więc poniżej standardowy tekst MIT z prawem autorskim z nuspec). Sprawdzono: paczka NuGet w lokalnej pamięci podręcznej (`~/.nuget/packages`), nuspec i pliki licencji sprawdzone 2026-10-20.

```text
MIT License

Copyright © Microsoft Corporation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## System.Runtime.CompilerServices.Unsafe (System.Runtime.CompilerServices.Unsafe.dll)

- Plik: `System.Runtime.CompilerServices.Unsafe.dll` (paczka NuGet 6.1.2, wydawca Microsoft, zależność pośrednia).
- Źródło: https://github.com/dotnet/maintenance-packages
- Licencja: MIT (nuspec: `license type="expression"` MIT; paczka nie zawiera osobnego pliku licencji, więc poniżej standardowy tekst MIT z prawem autorskim z nuspec). Sprawdzono: paczka NuGet w lokalnej pamięci podręcznej (`~/.nuget/packages`), nuspec i pliki licencji sprawdzone 2026-10-20.

```text
MIT License

Copyright © Microsoft Corporation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## System.Threading.Tasks.Extensions (System.Threading.Tasks.Extensions.dll)

- Plik: `System.Threading.Tasks.Extensions.dll` (paczka NuGet 4.6.3, wydawca Microsoft, zależność pośrednia).
- Źródło: https://github.com/dotnet/maintenance-packages
- Licencja: MIT (nuspec: `license type="expression"` MIT; paczka nie zawiera osobnego pliku licencji, więc poniżej standardowy tekst MIT z prawem autorskim z nuspec). Sprawdzono: paczka NuGet w lokalnej pamięci podręcznej (`~/.nuget/packages`), nuspec i pliki licencji sprawdzone 2026-10-20.

```text
MIT License

Copyright © Microsoft Corporation. All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## System.ComponentModel.Annotations (System.ComponentModel.Annotations.dll)

- Plik: `System.ComponentModel.Annotations.dll` (paczka NuGet 5.0.0, wydawca Microsoft, zależność pośrednia).
- Źródło: https://github.com/dotnet/runtime
- Licencja: MIT (`LICENSE.TXT` w paczce). Sprawdzono: paczka NuGet w lokalnej pamięci podręcznej (`~/.nuget/packages`), nuspec i pliki licencji sprawdzone 2026-10-20.

```text
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## PolySharp (kod generowany w naszych zestawach)

- Paczka NuGet 1.16.0 (analizator/generator, bez własnej biblioteki DLL w katalogu programu). Jego wygenerowany kod (polyfille nowszych funkcji C# i API dla net481) jest kompilowany do `DSOptimizer.Core.dll`, `DSOptimizer.Catalog.dll`, `DSOptimizer.Windows.dll` i `DSOptimizer.exe`.
- Źródło: https://github.com/Sergio0694/PolySharp/ (Sergio Pedri).
- Licencja: MIT (nuspec: `license type="expression"` MIT, „Copyright (c) 2026 Sergio Pedri”; paczka nie zawiera osobnego pliku licencji, więc poniżej standardowy tekst MIT). Sprawdzono: paczka NuGet w lokalnej pamięci podręcznej (`~/.nuget/packages`), nuspec i pliki licencji sprawdzone 2026-10-20.

```text
MIT License

Copyright (c) 2026 Sergio Pedri

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Costura (kod ładujący w wariancie jednego exe)

- Paczka NuGet `Costura.Fody` 6.2.0, dodatek do Fody używany wyłącznie przy budowaniu wariantu jednego exe (`/p:DsoSingleExe=true`, `tools/release/Build-Release.ps1 -Variant SingleExe`). Costura wstrzykuje do `DSOptimizer.exe` swój kod ładujący (inicjalizator modułu i obsługę `AssemblyResolve`), który wczytuje biblioteki programu osadzone w exe jako zwykłe, nieskompresowane zasoby. Wariant folderu (zip) nie zawiera kodu Costura.
- Fody 6.9.3 (https://github.com/Fody/Fody, MIT) to tylko narzędzie kompilacji – jego kod nie trafia do exe.
- Źródło: https://github.com/Fody/Costura (Simon Cropp i współtwórcy).
- Licencja: MIT (nuspec: `license type="expression"` MIT; paczka nie zawiera pliku licencji, więc poniżej tekst pliku `LICENSE` repozytorium). Sprawdzono: paczka NuGet w lokalnej pamięci podręcznej (`~/.nuget/packages`) i plik `LICENSE` repozytorium (GitHub API) 2026-10-26.

```text
The MIT License

Copyright (c) 2012 Simon Cropp and contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```

## Octicons – znak mark-github (ikona GitHub w pasku marki)

- Użycie: ścieżka wektorowa `mark-github` w wariancie 16 px, przeskalowana do siatki ikon programu (proporcjonalnie, bez zmiany kształtu).
- Źródło: https://github.com/primer/octicons, plik `icons/mark-github-16.svg` (https://raw.githubusercontent.com/primer/octicons/main/icons/mark-github-16.svg).
- Licencja Octicons: MIT, „Copyright (c) 2026 GitHub Inc.” (plik `LICENSE` repozytorium, pobrany 2026-10-20). Licencja MIT obejmuje kod i kształt ikon, ale nie daje praw do znaków towarowych GitHub.
- Zasady użycia logo GitHub (https://github.com/logos, przekierowanie na https://brand.github.com/foundations/logo, sprawdzono 2026-10-20): wolno użyć dozwolonego logo GitHub do linku do GitHub (np. Invertocat jako przycisk do profilu lub projektu) i mniej wyeksponowanego niż nazwa lub logo własnego produktu; nie wolno sugerować, że program pochodzi od GitHub ani że GitHub go popiera, używać logo GitHub jako logo własnego programu, ani modyfikować go (zmiana koloru, proporcji, łączenie z innymi elementami); logo ma być białe lub czarne.
- W DSOptimizer znak służy wyłącznie jako ikona linku do profilu autora (https://github.com/ds99-01), jest mniej wyeksponowany niż własny znak programu „DŚO”, nie jest logo programu i nie sugeruje powiązania z GitHub Inc. GITHUB, INVERTOCAT i OCTOCAT są znakami towarowymi GitHub, Inc.

```text
MIT License

Copyright (c) 2026 GitHub Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
