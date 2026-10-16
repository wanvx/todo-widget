; 待办清单 —— Inno Setup 打包脚本
; 用 tools\build-installer.ps1 构建，不要直接双击。
;
; 注意：本文件含中文，必须保存为「UTF-8 带 BOM」。
; Inno Setup 6 读无 BOM 的 UTF-8 会当成 ANSI，中文会变乱码。
; build-installer.ps1 会在编译前自动补 BOM，所以改完直接构建即可。

#define AppName "待办清单"
#define AppVersion "1.1.0"
#define AppPublisher "wanvx"
#define AppUrl "https://github.com/wanvx"
#define AppExeName "待办清单.exe"

[Setup]
; AppId 是这个软件的唯一身份：升级时靠它找到旧版本，改动它会导致装出两份。
AppId={{7C4E1B92-5D3A-4F6E-9A21-8B0D3E6F1A55}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
VersionInfoVersion=1.1.0.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} 安装程序
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

; 需求指定的默认安装位置；DisableDirPage=no 保证用户能自己改。
DefaultDirName=C:\代办事情清单
DisableDirPage=no
DisableProgramGroupPage=yes
DefaultGroupName={#AppName}
AllowNoIcons=yes

OutputDir=out
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
SetupIconFile=..\assets\logo.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

; 默认装到 C:\代办事情清单。实测非管理员账号也能在 C:\ 根目录建文件夹
; （默认 ACL 给 Users「创建文件夹」权限，创建者成为 owner 拿到完全控制），
; 所以这里用 lowest：双击即可安装，不弹 UAC。如果用户自己改成 Program Files
; 这类受保护目录，Inno 会直接报权限错误，不会静默装一半。
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

; 主程序是常驻托盘的小部件，安装/卸载前需要能把它关掉。
AppMutex=Local\TodoWidget.SingleInstance
CloseApplications=yes
RestartApplications=no

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "chs"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: checkedonce

[Dirs]
; 小部件会在自己旁边写 data.json / settings.json。装到 C:\ 根目录时创建者本来
; 就有完全控制，这里再显式授一次 Users 修改权限：万一目标文件夹是上一次被提权
; 安装留下的（owner 是 Administrators），普通权限运行的程序也不会被挡在外面
; 而静默退回到 %APPDATA%\TodoWidget。
Name: "{app}"; Permissions: users-modify

[Files]
Source: "..\dist\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "立即运行 {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
// 卸载时问一句要不要连任务数据一起删——直接删掉用户的待办列表太粗暴了。
//
// 用 SuppressibleMsgBox 而不是 MsgBox：静默卸载（unins000.exe /VERYSILENT）时
// MsgBox 会照常弹窗并卡住无人值守的卸载流程，SuppressibleMsgBox 则会直接返回
// 默认按钮。默认给 IDNO —— 静默卸载宁可保留用户数据，也不能误删。
function InitializeUninstall(): Boolean;
var
  DataFile: String;
  SettingsFile: String;
begin
  Result := True;

  DataFile := ExpandConstant('{app}\data.json');
  SettingsFile := ExpandConstant('{app}\settings.json');

  if FileExists(DataFile) or FileExists(SettingsFile) then
  begin
    if SuppressibleMsgBox('是否同时删除你的任务数据？' + #13#10 + #13#10 +
                          '（data.json 与 settings.json）' + #13#10 +
                          '选「否」会保留，方便以后重新安装时继续使用。',
                          mbConfirmation, MB_YESNO, IDNO) = IDYES then
    begin
      DeleteFile(DataFile);
      DeleteFile(SettingsFile);
    end;
  end;
end;
