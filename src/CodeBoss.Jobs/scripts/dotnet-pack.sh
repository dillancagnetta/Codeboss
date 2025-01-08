#!/bin/bash
echo Executing after success scripts on branch ${GITHUB_REF##*/}
echo Triggering Nuget package build

cd src/CodeBoss.Jobs/src/CodeBoss.Jobs
dotnet pack -c release /p:PackageVersion=8.0.$GITHUB_RUN_NUMBER --no-restore -o .

echo Uploading Codeboss.Jobs package to Nuget using branch ${GITHUB_REF##*/}

case "${GITHUB_REF##*/}" in
  "master")
    dotnet nuget push *.nupkg -k $NUGET_API_KEY -s https://api.nuget.org/v3/index.json --skip-duplicate
    ;;
esac
