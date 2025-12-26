dotnet build

Push-Location "C:\Program Files\StarMap"
try {
  .\StarMap.exe
} finally {
  Pop-Location
}
