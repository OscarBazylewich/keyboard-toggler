# Third-party notices

Keyboard Toggler's own code is under the [MIT License](LICENSE). The installer also bundles the
following third-party software, which keeps its own license.

## Interception

- Project: <https://github.com/oblitum/Interception>
- Version: 1.0.1
- Author: Francisco Lopes da Silva
- Files: `interception.dll` (user-mode library) and `install-interception.exe` (installs the
  `keyboard.sys`/`mouse.sys` filter drivers)
- License: **LGPL-3.0 for non-commercial use** (full text: [vendor/Interception-LGPL-3.0.txt](vendor/Interception-LGPL-3.0.txt)).
  Commercial use of Interception requires a license from its author; see the project page.

Keyboard Toggler loads `interception.dll` dynamically and does not modify it. You can replace it with
your own build of the library from the source linked above.
