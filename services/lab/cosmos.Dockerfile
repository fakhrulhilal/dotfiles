FROM azukaar/cosmos-server:latest-unstable
ENV VERSION_ID=12
RUN apt update && \
	apt install -y curl wget netcat-openbsd && \
	wget -q https://packages.microsoft.com/config/debian/$VERSION_ID/packages-microsoft-prod.deb && \
	dpkg -i packages-microsoft-prod.deb && \
	rm packages-microsoft-prod.deb
RUN apt update && apt install -y powershell
RUN pwsh -c "Install-Module posh-git"
