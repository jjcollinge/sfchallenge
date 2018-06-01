# Install powershell from Mac or Linux here: https://github.com/powershell/powershell
# Create a local cluster using Docker here: https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-get-started-mac
function New-TemporaryDirectory {
    $parent = [System.IO.Path]::GetTempPath()
    [string] $name = [System.Guid]::NewGuid()
    New-Item -ItemType Directory -Path (Join-Path $parent $name)
}

$publishingDir = "./publish"

Write-Host -ForegroundColor Blue "Cleaning existing publishing dir..."

if (Test-Path $publishingDir) {
    Remove-Item -Recurse $publishingDir -Force
}

New-Item -Path $publishingDir -ItemType Directory

Copy-Item -Path "./Exchange/ApplicationPackageRoot/*" -Recurse -Destination $publishingDir

Write-Host -ForegroundColor Blue "Building projects..."

$projects = Get-ChildItem **/*.csproj
foreach ($p in $projects){
    $projectDir = $p.Directory.FullName
    $packageRootDir = Join-Path $projectDir "PackageRoot"
    if (-Not (Test-Path $packageRootDir)){
        Write-Host -ForegroundColor Gray "Skipping $p as not SF service..."
        continue
    }

    ## Create code.zip and copy to publish dir
    $pkgFolderName = $p.Name -replace ".csproj", "Pkg"
    $projectPkgFolder = Join-Path $publishingDir $pkgFolderName
    New-Item -Path $projectPkgFolder -ItemType Directory

    #Create temp dir for build output
    $tempDir = New-TemporaryDirectory
    #Build service into staging dir    
    dotnet publish -c "RELEASE" -r "win7-x64" -o $tempDir.FullName $p.FullName

    #Compress the output into the publish dir for the service
    Compress-Archive -Path (Join-Path $tempDir.FullName "*")  -DestinationPath (Join-Path $projectPkgFolder "Code.zip") -CompressionLevel Optimal

    Remove-Item -Recurse $tempDir.FullName

    ## Create config.zip in publish dir
    Compress-Archive -Path (Join-Path (Join-Path $packageRootDir "Config") "*")  -DestinationPath (Join-Path $projectPkgFolder "Config.zip")
    
    ## Copy service manifest
    Copy-Item -Path (Join-Path $packageRootDir "ServiceManifest.xml") -Destination $projectPkgFolder
}
