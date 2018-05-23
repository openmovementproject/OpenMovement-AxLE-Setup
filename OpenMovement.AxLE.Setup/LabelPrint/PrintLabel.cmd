@ECHO OFF
CD /D %~dp0
IF %1!==! GOTO ERROR
IF NOT EXIST "%1" GOTO ERROR

SET wscript=wscript
if exist "%windir%\SysWoW64\cmd.exe" SET wscript="%windir%\SysWoW64\cmd.exe" /c wscript

@echo on
%wscript% "PrintLabel.vbs" %*
@echo off
GOTO END

:ERROR
ECHO Label file not specified or not found.  Example: Label.lbx objName 01:23:45:67:89:AB objBarcode 01:23:45:67:89:AB
GOTO END

:END
rem pause
