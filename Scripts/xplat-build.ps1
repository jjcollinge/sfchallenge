# Install powershell from Mac or Linux here: https://github.com/powershell/powershell
# Create a local cluster using Docker here: https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-get-started-mac
function New-TemporaryDirectory {
    $parent = [System.IO.Path]::GetTempPath()
    [string] $name = [System.Guid]::NewGuid()
    New-Item -ItemType Directory -Path (Join-Path $parent $name)
}

$publishDirPath = "${PSScriptRoot}/../publish"
$appPkgRootPath = "${PSScriptRoot}/../Exchange/ApplicationPackageRoot"
$appManifestPath = "${appPkgRootPath}/ApplicationManifest.xml"

$ErrorActionPreference = "Stop"

if (!(Test-Path $appManifestPath))
{
    Write-Host -ForegroundColor Red "Cannot find ApplicationManifest.xml at expected path: ${appManifestPath}"
    exit
}

if (Test-Path $publishDirPath) {
    Write-Host -ForegroundColor Blue "Cleaning existing publishing directory..."
    Remove-Item -Recurse $publishDirPath -Force
}

New-Item -Path $publishDirPath -ItemType Directory
Copy-Item -Path "${appPkgRootPath}/*" -Recurse -Destination $publishDirPath
Write-Host -ForegroundColor Blue "Building projects..."

$projects = Get-ChildItem "${PSScriptRoot}/../**/*.csproj"
foreach ($project in $projects){
    $projectDirPath = $project.Directory.FullName
    $packageRootDirPath = Join-Path $projectDirPath "PackageRoot"

    if (-Not (Test-Path $packageRootDirPath)){
        Write-Host -ForegroundColor Gray "Skipping $project as it is not a Service Fabric service..."
        continue
    }

    ## Create code.zip and copy to publish dir
    $pkgFolderName = $project.Name -replace ".csproj", "Pkg"
    $projectPkgFolder = Join-Path $publishDirPath $pkgFolderName
    New-Item -Path $projectPkgFolder -ItemType Directory

    #Create temp dir for build output
    $tempDir = New-TemporaryDirectory

    #Build service into staging dir    
    dotnet publish -c "RELEASE" -r "win7-x64" -o $tempDir.FullName $project.FullName

    #Compress the output into the publish dir for the service
    Compress-Archive -Path (Join-Path $tempDir.FullName "*")  -DestinationPath (Join-Path $projectPkgFolder "Code.zip") -CompressionLevel Optimal

    Remove-Item -Recurse $tempDir.FullName

    ## Create config.zip in publish dir
    Compress-Archive -Path (Join-Path (Join-Path $packageRootDirPath "Config") "*")  -DestinationPath (Join-Path $projectPkgFolder "Config.zip")
    
    ## Copy service manifest
    Copy-Item -Path (Join-Path $packageRootDirPath "ServiceManifest.xml") -Destination $projectPkgFolder
}

Write-Host -ForegroundColor Green "Packing complete, your Service Fabric application package can be found at:"
Write-Host -ForegroundColor Green "${publishDirPath}"
