# Third-Party Notices

NetBypass v1.0.0 does not bundle external Anti-DPI engines.

The project is designed to integrate tools such as GoodbyeDPI, ByeDPI and
zapret through adapters in future releases. When those integrations are added,
their binaries, licenses, notices, checksums and cleanup behavior must be
documented here.

## Planned External Engines

The following projects are not bundled in v1.0.0:

- GoodbyeDPI by ValdikSS — Apache-2.0 license.
- zapret by bol-van and related Windows distributions — license must be checked
  for the exact distribution used.
- ByeDPI / ByeByeDPI family — license must be checked for the exact project and
  binary used.

## Runtime Platform

NetBypass is built with .NET and WPF. Release artifacts may include Microsoft
.NET runtime components when published as self-contained Windows builds.
