$dist = "c:\Users\ritik2\Downloads\Shieldblock\dist"
$output = "c:\Users\ritik2\Downloads\Shieldblock\dist\SHA256SUMS.txt"
$files = Get-ChildItem -Path "$dist\*.exe"

$lines = @()
foreach ($f in $files) {
    $h = (Get-FileHash -Path $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines += "$h  $($f.Name)"
}

$lines | Out-File -FilePath $output -Encoding utf8
Get-Content $output
