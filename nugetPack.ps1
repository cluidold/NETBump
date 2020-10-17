dotnet pack ./src/NETBump/NETBump.csproj -c:Release -o:./nuget
Copy-Item ./nuget/*.* -Destination "C:\Development\nuget" -Recurse -force