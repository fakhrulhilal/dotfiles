{ lib
, stdenv
, fetchurl
, xar
, cpio
, makeWrapper
}:

let
  pname = "teams";
  versions = {
    darwin = "24256.2503.3156.9924";
  };
  hashes = {
    darwin = "sha256-775415f70fb60067467ab963e9e361036964f5f1b6cce7bb3f591f4eda88fd62";
  };
  meta = with lib; {
    description = "Microsoft Teams";
    homepage = "https://teams.microsoft.com";
    downloadPage = "https://teams.microsoft.com/downloads";
    sourceProvenance = with sourceTypes; [ binaryNativeCode ];
    license = licenses.unfree;
    maintainers = with maintainers; [ tricktron ];
    platforms = [ "x86_64-darwin" "aarch64-darwin" ];
    mainProgram = "teams";
  };

  appName = "Teams.app";
in
stdenv.mkDerivation {
  inherit pname meta;
  version = versions.darwin;

  src = fetchurl {
    url = "https://statics.teams.cdn.office.net/production-osx/enterprise/webview2/lkg/MicrosoftTeams.pkg";
    hash = hashes.darwin;
  };

  nativeBuildInputs = [ xar cpio makeWrapper ];

  unpackPhase = ''
    xar -xf $src
    zcat < Teams_osx_app.pkg/Payload | cpio -i
  '';

  sourceRoot = "Microsoft\ Teams.app";
  dontPatch = true;
  dontConfigure = true;
  dontBuild = true;

  installPhase = ''
    runHook preInstall
    mkdir -p $out/{Applications/${appName},bin}
    cp -R . $out/Applications/${appName}
    makeWrapper $out/Applications/${appName}/Contents/MacOS/Teams $out/bin/teams
    runHook postInstall
  '';
}
