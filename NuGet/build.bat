@echo off
copy ..\bin\Release\DScript.dll lib\net40\
nuget.exe pack spec.nuspec
pause