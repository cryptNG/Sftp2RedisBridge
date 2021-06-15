$version="1.4"

Write-Host "Login before deployment"
docker login -u cryptng

Write-Host ""
Write-Host "#####"
Write-Host "Building NanoServer 20H2 tagged $version-debbuster-slim-5"
Write-Host "#####"
Write-Host ""

docker build  -f Dockerfile_Deb_BusterSlim50 -t cryptng/sftp2redisbridge:$version-debbuster-slim-5 .


Write-Host ""
Write-Host "#####"
Write-Host "Building NanoServer 20H2 tagged $version-nanoserver-20h2"
Write-Host "#####"
Write-Host ""

docker build  -f Dockerfile_Win_NanoServer20H2 -t cryptng/sftp2redisbridge:$version-nanoserver-20h2 .


Write-Host ""
Write-Host "#####"
Write-Host "Building Windows Server Core LTSC 2019 tagged $version-windowsservercore-ltsc2019"
Write-Host "#####"
Write-Host ""

docker build -f Dockerfile_Win_CoreLtsc2019 -t cryptng/sftp2redisbridge:$version-windowsservercore-ltsc2019 . 

Write-Host ""
Write-Host "#####"
Write-Host "Build finished, publishing cryptng/sftp2redisbridge:$version-debbuster-slim-5"

Write-Host "#####"
Write-Host ""
docker push cryptng/sftp2redisbridge:$version-debbuster-slim-5


Write-Host ""
Write-Host "#####"
Write-Host "Build finished, publishing cryptng/sftp2redisbridge:$version-nanoserver-20h2"

Write-Host "#####"
Write-Host ""
docker push cryptng/sftp2redisbridge:$version-nanoserver-20h2

Write-Host ""
Write-Host "#####"
Write-Host "Build finished, publishing cryptng/sftp2redisbridge:$version-windowsservercore-ltsc2019"

Write-Host "#####"
Write-Host ""
docker push cryptng/sftp2redisbridge:$version-windowsservercore-ltsc2019
Write-Host ""
Write-Host "#####"
Write-Host "Finished"
Write-Host "Press any key to exit"
Write-Host "#####"
pause