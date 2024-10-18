{
  description = "MacOS development setup";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixpkgs-unstable";
    nix-darwin.url = "github:LnL7/nix-darwin";
    nix-darwin.inputs.nixpkgs.follows = "nixpkgs";
    app-msteams.url = "./apps/teams";
    app-msteams.flake = false;
  };

  outputs = inputs@{ self, nix-darwin, nixpkgs, app-msteams }:
  let
    configuration = { pkgs, ... }: {
      nixpkgs.config.allowUnfree = true;
      # List packages installed in system profile. To search by name, run:
      # $ nix-env -qaP | grep wget
      packages.systems.default =
      # Notice the reference to nixpkgs here.
        with import nixpkgs { system = "aarch64-darwin"; };
        stdenv.mkDerivation {
          name = "hello";
          src = self;
          buildInputs = [
            (callPackage app-msteams {}) # callPackage because is not a flake
             # or dumbdep.packages."${system}".default # if it was a flake.
          ];
        };
      environment.systemPackages =
      [ 
        pkgs.vim
        pkgs.mkalias
        pkgs.delta
        pkgs.curl
        pkgs.wget
        pkgs.vscode
        pkgs.teams
        #pkgs.jetbrains.rider
      ];

      fonts.packages = 
      [
        (pkgs.nerdfonts.override { fonts = [ "JetBrainsMono" ]; })
      ];

      # reference: https://mynixos.com/search?q=system
      system.defaults = {
        dock.autohide = true;
        dock.mineffect = "genie";
        dock.magnification = true;
        dock.persistent-apps = [
          "/System/Applications/Mail.app"
          "/System/Applications/Messages.app"
          "/Applications/Safari.app"
          "/Applications/Microsoft Edge.app"
          "/Applications/Microsoft Teams.app"
          # JB Rider
          # System Settings
        ];
        loginwindow.GuestEnabled = false;
        NSGlobalDomain = {
          #AppleLocale = "id_ID";
          #AppleLanguages = [ "id" "en" "ar" ];
          AppleICUForce24HourTime = true;
        };
        menuExtraClock = {
          Show24Hour = true;
          ShowDayOfWeek = true;
        };
        smb.NetBIOSName = "iroel-MBP";
        # enable firewall
        alf.globalstate = 1;
        finder = {
          ShowPathbar = true;
        };
        CustomUserPreferences = {
          "com.apple.Safari" = {
            "com.apple.Safari.ContentPageGroupIdentifier.WebKit2DeveloperExtrasEnabled" = true;
          };
        };
      };
      
      security.pam.enableSudoTouchIdAuth = true;
      # Auto upgrade nix package and the daemon service.
      services.nix-daemon.enable = true;
      # nix.package = pkgs.nix;

      # Necessary for using flakes on this system.
      nix.settings.experimental-features = "nix-command flakes";

      # Create /etc/zshrc that loads the nix-darwin environment.
      programs.zsh.enable = true;  # default shell on catalina
      # programs.fish.enable = true;
      

      # Set Git commit hash for darwin-version.
      system.configurationRevision = self.rev or self.dirtyRev or null;

      # Used for backwards compatibility, please read the changelog before changing.
      # $ darwin-rebuild changelog
      system.stateVersion = 5;

      # The platform the configuration will be used on.
      nixpkgs.hostPlatform = "aarch64-darwin";
    };
  in
  {
    # Build darwin flake using:
    # $ darwin-rebuild build --flake .#simple
    darwinConfigurations."mbp" = nix-darwin.lib.darwinSystem {
      modules = [ configuration ];
    };

    # Expose the package set, including overlays, for convenience.
    darwinPackages = self.darwinConfigurations."mbp".pkgs;
  };
}
