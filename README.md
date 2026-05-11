# Linqq Xray VPN

A modern, native Windows GUI client for **Xray core**, built with WinUI 3.

## Features

- Support for **Shadowsocks, VMess, VLESS, Trojan, Hysteria2**
- Full **TUN Mode** support
- Subscription import and auto-update
- Advanced custom routing rules (geoip / geosite)
- Auto-start on boot + Auto-connect
- **Multi-language support** (English / Russian) with instant language switching
- Highly customizable UI with theme and protocol color settings
- Compact mini mode for tray

## This Project

This is a **fork** of the original [XrayUI-dev](https://github.com/PhoenixNil/XrayUI-dev) project.

Huge thanks to **[PhoenixNil](https://github.com/PhoenixNil)** for creating the original codebase and for the excellent foundation.

## Download

Get the latest release from the **[Releases page](https://github.com/Stiv455/Linqq-XrayUI/releases)**.

## Build from Source

```bash
git clone https://github.com/Stiv455/Linqq-XrayUI.git
cd Linqq-XrayUI

dotnet build -c Release

# Publish for your architecture
dotnet publish -c Release -r win-x64 --self-contained false
