# Dependency review

Reviewed 2026-09-17 before foundation delivery. Exact versions are centralized in
Directory.Packages.props; packages.lock.json records resolved transitive dependencies.

| Package | License | Maintenance/platform assessment | Cost |
|---|---|---|---|
| System.CommandLine 2.0.12 | MIT | Microsoft dotnet project; current stable release, POSIX/Windows parsing; net8.0 asset selected for net10.0 | No transitive runtime dependencies on this target |
| MSTest.TestFramework 4.4.1 | MIT | Microsoft-supported testfx project; modern .NET targets | Development only; includes analyzers |
| MSTest.TestAdapter 4.4.1 | MIT | Same maintained testfx project | Development-only test platform dependencies |
| Microsoft.NET.Test.Sdk 18.10.1 | MIT | Microsoft test platform | Development-only runner/object model/coverage dependencies |

License and dependency metadata are checked from the restored official NuGet packages.
HTTP operations use the BCL. Native credential access uses Windows Advapi32 and macOS
Security system frameworks; Linux dynamically loads libsecret/GLib/GIO/GObject from the
OS. libsecret is LGPL-2.1-or-later, maintained by GNOME; system library updates remain
the host administrator's responsibility. These libraries are not bundled in the tool.
Native interop adds no NuGet dependency. macOS's generic-password API is deprecated;
its replacement and target-OS execution are explicit hardening work. No Native AOT
claim is made. Linux requires a desktop Secret Service/session bus for that provider.

Security gate: `dotnet list Ado.slnx package --vulnerable --include-transitive`, NuGet
audit for all dependencies on restore, and committed lock files. An advisory scan is
not a guarantee of absence of vulnerabilities. Repeat on dependency upgrades and CI.

Sources: [System.CommandLine](https://www.nuget.org/packages/System.CommandLine/2.0.12),
[MSTest](https://www.nuget.org/packages/MSTest.TestFramework/4.4.1),
[testfx license](https://github.com/microsoft/testfx/blob/main/LICENSE).
