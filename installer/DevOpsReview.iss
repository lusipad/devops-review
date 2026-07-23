#ifndef AppVersion
  #error AppVersion must be provided by the build script
#endif

#ifndef PackageRoot
  #error PackageRoot must be provided by the build script
#endif

[Setup]
AppId={{B43D67B4-654A-4DE5-B941-3541698FB43D}
AppName=DevOps Review
AppVersion={#AppVersion}
AppPublisher=lusipad
AppPublisherURL=https://github.com/lusipad/devops-review
DefaultDirName={localappdata}\Programs\DevOpsReview\app
DefaultGroupName=DevOps Review
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=DevOpsReview-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=DevOps Review
VersionInfoVersion={#AppVersion}
VersionInfoCompany=lusipad
VersionInfoDescription=Azure DevOps Codex Review installer
VersionInfoProductName=DevOps Review
VersionInfoProductVersion={#AppVersion}

[Tasks]
Name: "chrome"; Description: "为 Google Chrome 注册本地 Bridge"; GroupDescription: "浏览器："
Name: "edge"; Description: "为 Microsoft Edge 注册本地 Bridge"; GroupDescription: "浏览器："

[Files]
Source: "{#PackageRoot}\bridge\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageRoot}\DevOpsReview.Configurator.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageRoot}\extension\*"; DestDir: "{app}\extension"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageRoot}\scripts\*"; DestDir: "{app}\scripts"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageRoot}\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageRoot}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageRoot}\CHANGELOG.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageRoot}\SHA256SUMS.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageRoot}\config.example.json"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKCU; Subkey: "Software\Google\Chrome\NativeMessagingHosts\com.lus.devops_review"; ValueType: string; ValueName: ""; ValueData: "{app}\com.lus.devops_review.json"; Tasks: chrome; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\com.lus.devops_review"; ValueType: string; ValueName: ""; ValueData: "{app}\com.lus.devops_review.json"; Tasks: edge; Flags: uninsdeletekey

[Icons]
Name: "{group}\配置 DevOps Review"; Filename: "{app}\DevOpsReview.Configurator.exe"

[Run]
Filename: "{app}\DevOpsReview.Configurator.exe"; Description: "配置 Azure DevOps 仓库"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\com.lus.devops_review.json"

[Code]
function JsonEscape(const Value: String): String;
begin
  Result := Value;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

procedure WriteNativeMessagingManifest;
var
  Manifest: TArrayOfString;
begin
  SetArrayLength(Manifest, 7);
  Manifest[0] := '{';
  Manifest[1] := '  "name": "com.lus.devops_review",';
  Manifest[2] := '  "description": "Local Codex bridge for Azure DevOps pull request review",';
  Manifest[3] := '  "path": "' + JsonEscape(ExpandConstant('{app}\DevOpsReview.Bridge.exe')) + '",';
  Manifest[4] := '  "type": "stdio",';
  Manifest[5] := '  "allowed_origins": ["chrome-extension://kldpfliioeaahafemncagclpehbnblig/"]';
  Manifest[6] := '}';

  if not SaveStringsToUTF8FileWithoutBOM(
    ExpandConstant('{app}\com.lus.devops_review.json'),
    Manifest,
    False) then
  begin
    RaiseException('无法写入 Native Messaging manifest。');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WriteNativeMessagingManifest;
  end;
end;
