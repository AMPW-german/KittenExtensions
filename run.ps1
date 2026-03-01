dotnet build

Push-Location "C:\Program Files\StarMap"
try {
  .\StarMap.Loader.exe
} finally {
  Pop-Location
}
