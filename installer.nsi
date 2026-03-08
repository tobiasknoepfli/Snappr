!include "MUI2.nsh"

Name "Snappr"
OutFile "Publish\V1.2.0\Snappr_Installer_V1.2.0.exe"
InstallDir "$PROGRAMFILES64\Snappr"
InstallDirRegKey HKCU "Software\Snappr" ""

RequestExecutionLevel admin

!define MUI_ABORTWARNING
!define MUI_ICON "Assets\favicon.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "Snappr Application" SecApp
    SectionIn RO
    SetOutPath "$INSTDIR"
    File /r "Publish\V1.2.0\bin\*.*"

    WriteUninstaller "$INSTDIR\uninstall.exe"
    WriteRegStr HKCU "Software\Snappr" "" $INSTDIR
SectionEnd

Section "Desktop Shortcut" SecDesktop
    CreateShortcut "$DESKTOP\Snappr.lnk" "$INSTDIR\Snappr.exe" "" "$INSTDIR\Snappr.exe" 0
SectionEnd

Section "Start Menu Shortcut" SecStartMenu
    CreateDirectory "$SMPROGRAMS\Snappr"
    CreateShortcut "$SMPROGRAMS\Snappr\Snappr.lnk" "$INSTDIR\Snappr.exe" "" "$INSTDIR\Snappr.exe" 0
    CreateShortcut "$SMPROGRAMS\Snappr\Uninstall Snappr.lnk" "$INSTDIR\uninstall.exe"
SectionEnd

Section "Uninstall"
    Delete "$DESKTOP\Snappr.lnk"
    RMDir /r "$SMPROGRAMS\Snappr"
    RMDir /r "$INSTDIR"
    DeleteRegKey HKCU "Software\Snappr"
SectionEnd

LangString DESC_SecApp ${LANG_ENGLISH} "The main application files."
LangString DESC_SecDesktop ${LANG_ENGLISH} "Create a shortcut on your desktop."
LangString DESC_SecStartMenu ${LANG_ENGLISH} "Create a shortcut in the Start Menu (can be pinned to Taskbar)."

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecApp} $(DESC_SecApp)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} $(DESC_SecDesktop)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecStartMenu} $(DESC_SecStartMenu)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

