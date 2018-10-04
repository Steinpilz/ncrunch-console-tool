#r "paket:
nuget FSharp.Data
nuget System.IO.Compression.ZipFile
nuget Fake.DotNet.Cli
//"
#load ".fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open FSharp.Data

module HtmlDocument =
    let cssSelect (selector: string) (htmlDoc: HtmlDocument) = htmlDoc.CssSelect (selector)

module HtmlNode =
    let cssSelect (selector: string) (htmlNode: HtmlNode) = htmlNode.CssSelect (selector)

module String =
    let contains (substr: string) (s: string) = s.Contains (substr)

type NCrunchVer = { Major: int; Minor: int }
let parseNCrunchVer (str: string) =
    let parts = str.Split('.')
    if parts.Length <> 2 then failwithf "bad NCrunch version %s" <| str
    { Major = parts.[0] |> int; Minor = parts.[1] |> int }
let ncrunchVerToSemVer ncrunchVer = sprintf "%d.%d.0" <| ncrunchVer.Major <| ncrunchVer.Minor

let getToolUrl () =
    let isLatestRelease releaseNode =
        releaseNode
        |> HtmlNode.cssSelect "div.latestStableRelease"
        |> (not << Seq.isEmpty)

    let isToolEntry entryNode =
        entryNode
        |> HtmlNode.cssSelect "span.visualStudioVersion"
        |> List.head
        |> HtmlNode.innerText
        |> ((=) "Console Tool")

    let siteBaseUrl = "https://www.ncrunch.net"
    let createUri url = System.Uri (url, System.UriKind.RelativeOrAbsolute)
    let combineUrls x y = System.Uri (createUri <| x, createUri <| y) |> string
    let getSiteUrl url = combineUrls siteBaseUrl url

    let extractDownloadPageUrl entryNode =
        entryNode
        |> HtmlNode.cssSelect "a"
        |> Seq.find (HtmlNode.innerText >> (String.contains "Download ZIP"))
        |> HtmlNode.attributeValue "href"
        |> getSiteUrl
    
    let extractVersionFromDownloadPageUrl downloadPageUrl =
        downloadPageUrl
        |> createUri
        |> (fun uri -> uri.Query)
        |> System.Web.HttpUtility.ParseQueryString
        |> (fun query -> query.Item "version")
        |> parseNCrunchVer
    
    let extractDownloadUrl downloadPageDoc =
        downloadPageDoc
        |> HtmlDocument.cssSelect "a"
        |> Seq.find (HtmlNode.innerText >> (String.contains "try this link instead"))
        |> HtmlNode.attributeValue "href"
        |> getSiteUrl


    getSiteUrl "download"
    |> HtmlDocument.Load
    |> HtmlDocument.cssSelect "div.releaseContainer"
    |> Seq.find isLatestRelease // it is first currently (30.09.2018)
    |> HtmlNode.cssSelect "div.downloadEntry"
    |> Seq.find isToolEntry
    |> extractDownloadPageUrl
    |> (fun downloadPageUrl ->
        let version = downloadPageUrl |> extractVersionFromDownloadPageUrl
        let downloadZipUrl = downloadPageUrl |> HtmlDocument.Load |> extractDownloadUrl
        (version, downloadZipUrl)
    )

let downloadToolZip (zipUrl: string) (zipPath: string) =
    use wc = new System.Net.WebClient ()
    do wc.DownloadFile (zipUrl, zipPath)

let (toolVer, toolUrl) = getToolUrl ()


let generateNuspec toolVer =
    let version = toolVer |> ncrunchVerToSemVer
    let template = @"<?xml version=""1.0""?>
<package xmlns=""http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd"">
    <metadata minClientVersion=""2.12"">
        <id>NCrunch.ConsoleTool</id>
        <version>{version}</version>
        <repository type=""git"" url=""https://github.com/Steinpilz/ncrunch-console-tool"" />
        <title>NCrunch.ConsoleTool</title>
        <authors>Remco</authors>
        <summary>NCrunch.ConsoleTool</summary>
        <description>NCrunch.ConsoleTool</description>
        <language>en-US</language>
        <requireLicenseAcceptance>false</requireLicenseAcceptance>
        <developmentDependency>true</developmentDependency>
    </metadata>
    <files>
        <file src=""NCrunch Console Tool/**"" target=""tools"" />
    </files>
</package>"
    template.Replace("{version}", version)

let generatePackCsproj _ = @"<Project Sdk=""Microsoft.NET.Sdk"">
    <PropertyGroup>
        <TargetFramework>netstandard2.0</TargetFramework>
        <NuspecFile>NCrunch.ConsoleTool.nuspec</NuspecFile>
    </PropertyGroup>
</Project>"


let cd = System.Environment.CurrentDirectory
let zipFile = cd @@ "NCrunch.ConsoleTool.zip"
let zipUnpackDir = cd @@ "NCrunch.ConsoleTool"

// TODO: it can be done better
do downloadToolZip <| toolUrl <| zipFile
try
    do System.IO.Compression.ZipFile.ExtractToDirectory (zipFile, zipUnpackDir)
    try
        let ncrunchToolDir = zipUnpackDir |> System.IO.Directory.GetDirectories |> System.Linq.Enumerable.Single
        let ncrunchToolDirName = ncrunchToolDir |> System.IO.Path.GetFileName
        if ncrunchToolDirName <> "NCrunch Console Tool" then failwithf "downloaded zip has bad dir name %s" <| ncrunchToolDirName
        let packNuspecFile = zipUnpackDir @@ "NCrunch.ConsoleTool.nuspec"
        let packCsprojFile = zipUnpackDir @@ "NCrunch.ConsoleTool.csproj"
        do toolVer |> generateNuspec |> File.writeString false packNuspecFile
        do () |> generatePackCsproj |> File.writeString false packCsprojFile
        do packCsprojFile |> DotNet.pack (fun cfg -> { cfg with OutputPath = Some zipUnpackDir })
        let packFileName = toolVer |> ncrunchVerToSemVer |> sprintf "NCrunch.ConsoleTool.%s.nupkg"
        let packFile = zipUnpackDir @@ packFileName

        let nugetSource = "https://api.nuget.org/v3/index.json"
        let nugetApiKey = Environment.environVarOrFail "NUGET_API_KEY"
        let dotnetNugetPushCmd = sprintf "push %s -s %s -k %s" <| packFile <| nugetSource <| nugetApiKey
        let pushRes = DotNet.exec (fun cfg -> { cfg with WorkingDirectory = ncrunchToolDir }) "nuget" dotnetNugetPushCmd
        if not pushRes.OK then failwith "push failed"
    finally
        do Directory.delete <| zipUnpackDir
finally
    do File.delete <| zipFile
