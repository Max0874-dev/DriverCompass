# Third-Party Notices

DriverCompass is licensed under the MIT license (see [LICENSE](LICENSE)). It includes or uses the following third-party components.

## Bundled data

### pci.ids – The PCI ID Repository

- Source: https://pci-ids.ucw.cz/ (file `data/third-party/pci.ids`, embedded into `DriverCompass.Data`)
- Maintained by Albert Pool, Martin Mares, and other volunteers from the PCI ID Project.
- The file "can be distributed under either the GNU General Public License (version 2 or higher) or the 3-clause BSD License". **DriverCompass uses it under the 3-clause BSD License:**

```
Copyright (c) the PCI ID Project contributors (https://pci-ids.ucw.cz/)
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice,
   this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.
3. Neither the name of the copyright holder nor the names of its contributors
   may be used to endorse or promote products derived from this software
   without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### usb.ids – not included

The USB ID list (linux-usb.org) does not state a license in its header, so its redistribution terms are unclear. It is therefore **not** bundled. USB vendors come from DriverCompass's own MIT-licensed `data/vendors.json`.

## Data downloaded at runtime (not bundled)

The optional version check (stage 2) reads official catalogs directly from the vendors (Microsoft Windows Update, Dell, Lenovo, HP) when the user turns a source on and clicks "Check versions". These files are not part of DriverCompass and are not redistributed; they are cached locally for the user only.

## NuGet packages

| Package | License |
|---|---|
| Microsoft.WindowsAppSDK.WinUI (+ .Base, .Foundation, .InteractiveExperiences) | Windows App SDK – license terms shipped in each package (redistributable runtime components) |
| Microsoft.Web.WebView2 (dependency of WinUI) | BSD-3-Clause-style license shipped in the package |
| Microsoft.Windows.SDK.BuildTools | Microsoft Software License Terms (build-time only, not redistributed) |
| CommunityToolkit.Mvvm | MIT |
| Microsoft.Extensions.DependencyInjection / Logging | MIT |
| System.Management | MIT |
| Serilog, Serilog.Extensions.Logging, Serilog.Sinks.File | Apache-2.0 |
| JsonSchema.Net (json-everything) | MIT |
| xunit.v3 (tests only) | Apache-2.0 |

The full license texts are available on nuget.org and in each package. Release archives must include them, see [docs/release-checklist.md](docs/release-checklist.md).

## Trademarks

Manufacturer names are used only to identify hardware and link to official support pages. DriverCompass is not affiliated with or endorsed by any of them.
