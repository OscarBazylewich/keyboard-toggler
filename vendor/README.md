# Files from Interception

The files in this folder are from **Interception v1.0.1**. Francisco Lopes da Silva made Interception. We did not change these files.

We got the files from `Interception.zip` on this page:
<https://github.com/oblitum/Interception/releases/tag/v1.0.1>

The SHA-256 of `Interception.zip` is `ad038963d6413055765128b0b931f6e765147c9916dba79e65d872b261f9af10`.

| File | Location in `Interception.zip` | SHA-256 |
|---|---|---|
| `install-interception.exe` | `command line installer/install-interception.exe` | `e137863a79da797f08e7a137280ff2a123809044a888fd75ce9c973198915abe` |
| `interception.dll` | `library/x64/interception.dll` | `ab88164c11b1b48488772d4c3bfaa4509d5b0ae9dbc5a691dc4f96f0260443c8` |
| `Interception-LGPL-3.0.txt` | `licenses/non-commercial-usage/LGPL 3.0.txt` | `da7eabb7bafdf7d3ae5e9f223aa5bdc1eece45ac569dc21b3b037520b4464768` |

The license of Interception is **LGPL-3.0 for non-commercial use**. For commercial use, you must get a different license from Francisco Lopes da Silva.
For more information, refer to [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

To make sure that the files are correct, do these steps:

1. Open PowerShell in the root folder of the repository.
2. Type this command:

   ```powershell
   Get-FileHash vendor\* -Algorithm SHA256
   ```

3. For each file in the table, make sure that the SHA-256 in the output agrees with the value in the table.
