#!/bin/bash
echo Executing after success scripts on branch ${GITHUB_REF##*/}
echo Triggering Nuget package build

cd src/CodeBoss.MultiTenant/src/CodeBoss.MultiTenant
dotnet pack -c release /p:PackageVersion=0.1.$GITHUB_RUN_NUMBER --no-restore -o .

echo Uploading Codeboss.MultiTenant package to Nuget using branch ${GITHUB_REF##*/}

case "${GITHUB_REF##*/}" in
  "master")
    dotnet nuget push *.nupkg -k $NUGET_API_KEY -s https://api.nuget.org/v3/index.json
    ;;
esac
