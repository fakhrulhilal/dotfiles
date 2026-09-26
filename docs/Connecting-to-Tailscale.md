# Connecting to Tailscale

This guide is an alternative to ssh to tailscale without installing GUI app.

1. Create [auth key](https://console.tailscale.com/admin/settings/keys), and expose as `TAILSCALE_AUTH_KEY` environment variable
2. Run `ts_connect`, it will require approval at the first time, login with your tailscale console to approve
3. Run `ts_ssh <host>` to connect for ssh to `<host>`, be sure to add your tailscale magic domain name (suffix: *.ts.net)
4. Run `ts_exit` to exit tailscale preserving existing state, no need for approval for next time
5. Run `ts_status --logout` to logout from tailscale completely, will require approval for next time