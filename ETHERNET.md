# Ethernet inspection

This tool began as a way to investigate repurposing older routers as small web servers for local pages. It collects information about existing services and management interfaces. Deploying a web server or changing firmware is future work.

# JavaScript router inspection in 0.6.18

Open **Work on code > Ethernet inspection** and enter the router IPv4, port and credentials. Choose **Open JavaScript browser**. Microsoft Edge must be installed. A separate Edge profile and window open for this router. Browse its menus, then use **Capture page for AI** in the inspection window. Close that window to end the browser session and append the capture to the network report. Choose **Save analysis project** to provide the report to the local assistant.

The capture includes rendered text, links and control labels from the main page and accessible frames. Form values are omitted, and entered credentials are redacted. Authentication is handled in memory. The isolated profile is removed when the session ends; any cleanup failure is reported with the folder path. Requests are restricted to GET/HEAD on the original router scheme, address and port. Requests to other origins and POST submissions are blocked. GET endpoints can still trigger actions on old devices, so inspect menu labels before navigating. This browser has no autonomous AI clicking or configuration submission tool. The real Netgear workflow needs testing on the laptop.

Development checks launch actual Edge against an authenticated local fixture, execute JavaScript, capture an iframe, verify that POST is blocked and that credentials are omitted, then check isolated-profile cleanup.
# Router login and shell files in 0.6.17

In **Work on code > Ethernet inspection**, enter the router IPv4, username and password. Choose **Log in and read** for HTTP Basic authentication. The tool requests the landing page first and sends the entered credentials once only when the same device challenges with Basic. Other authentication methods and browser form logins are reported as unsupported. The password field is masked. Credentials are held in memory and omitted from the saved report. HTTP Basic sends them unencrypted over the selected local Ethernet connection.

**List shell files** connects to an already available Telnet shell on port 23 with the entered credentials. Once a shell prompt is recognized, it runs one fixed `ls -la` inventory of `/`, `/bin`, `/sbin`, `/etc`, `/www`, `/www/htdocs`, and `/usr/sbin`. Output is limited to 64 KiB and the connection to 30 seconds. The report can be saved as an analysis project for the local model. This inventories directory names and file metadata, not full file contents. Web login and shell login may require different credentials. Router-side Telnet enablement, SSH, firmware flashing and arbitrary shell commands are outside this release. Only use your isolated local device for these unencrypted protocols.

Development validation uses local HTTP and Telnet fixtures. The real Netgear must still be tested on the laptop.
# Router page inspection in BoltWorks Local AI 0.6.16

Open **Work on code > Ethernet inspection**. After identifying the router IPv4 address, choose port 80 or 8080 and select **Read router page**. This sends one HTTP GET for `/`, bound to the selected wired adapter. The report includes response headers, HTTP login requirements and extracted page text. Cookies and script/style content are omitted. Redirects are reported without following them. Reading is bounded to eight seconds and 64 KiB.

Choose **Save analysis project** to give the local code assistant the report. The device text is marked as untrusted observations. A password-protected router may return only HTTP 401 and its authentication scheme. This version does not authenticate, submit forms, discover hidden pages, run shell commands, flash firmware or provide an interactive browser session. The router itself must be tested separately. The development checks use a local HTTP fixture.
# Mini REA 0.2: Ethernet inspection

In LlamaLite 0.6.7 open **Work on code > Ethernet inspection**. This feature works without a model, Node, internet access or a packet-capture driver. It uses Windows adapter information and the IPv4 ARP cache. It is not a packet sniffer or an authentication/exploitation tool.

## First connection on the offline laptop

1. Power the device and connect the laptop to one of its LAN ports. Use an isolated direct connection for an unfamiliar device. A WAN/uplink port may not offer management access. Old equipment may require a suitable cable type; link lights are the first check.
2. Choose **Read adapters / cache**. This reads Windows configuration/cache without sending discovery probes. It shows active Ethernet IPv4 addresses, masks, gateways and cached IP/MAC entries. Virtual Ethernet interfaces may also appear. Entries may be stale, and an empty cache does not prove the device is absent. This feature does not identify hardware models from MAC addresses.
3. Find the device's actual management address from its label/manual, a gateway assignment, or a relevant cache entry. Do not assume all routers use the same address. A cached gateway is only a candidate, not proof of identity.
4. Enter that address and select **Check this IP**. If multiple wired interfaces match, also enter the laptop's wired IPv4 address. The checker only accepts private/link-local IPv4 peers in a currently active wired subnet, rejects the laptop's own address and subnet network/broadcast addresses, and binds its TCP socket to the selected laptop address.
5. Save the report as a new analysis project if you want your locked local model to explain the observations. Saving does not itself run a model or make more connections.

If the router does not provide DHCP, the laptop may need a manual IPv4 address in the device's known subnet. The tool never changes Windows settings, resets the device or assumes a default subnet. For example, only when documentation establishes a device at 192.168.1.1 with mask 255.255.255.0 could a free laptop address such as 192.168.1.2 with that mask be appropriate. A 169.254.x.x laptop address will not match that device's subnet. No gateway/DNS is required for a direct same-subnet conversation. Record existing laptop settings before manually changing them. [OpenWrt's device-specific recovery guidance illustrates the need for matching addressing](https://openwrt.org/docs/guide-user/troubleshooting/failsafe_and_factory_reset).

## What a connection check means

The tool tries TCP ports 80, 443, 22, 23 and 8080, sequentially, with a 1.2-second timeout each. These commonly host HTTP, HTTPS, SSH, Telnet and alternate HTTP respectively, but this tool does not verify the protocol. Results distinguish accepted connections, explicit refusal, timeouts and socket errors. A timeout can mean filtering, a slow response, an addressing problem or an offline host. It is not a finding of an open or closed port.

It sends no passwords, login attempts, HTTP requests, application commands, subnet sweeps or firmware writes. A normal TCP connection can still appear in the device's logs. Stop cancels the check. Windows routing still applies; source binding is not network isolation. Use a direct isolated cable setup and avoid ambiguous overlapping networks when investigating unfamiliar equipment.

If the web interface is reachable, you can open its address in your browser and use credentials you already have. SSH additionally needs a suitable client and a permitted account; this tool does not provide an interactive shell. An open port does not reveal a vulnerability or establish access to firmware. An unmanaged hub/switch generally has no software management endpoint; a managed switch may have one. A USB modem is covered only if it exposes a Windows Ethernet-style interface; serial-only devices and non-Ethernet USB protocols need a separate adapter.

## Standalone commands

```powershell
.\mini-rea.exe network
.\mini-rea.exe probe 192.168.1.1
# Optional third argument chooses the laptop's local wired address:
.\mini-rea.exe probe 192.168.1.1 192.168.1.2
```

These addresses are examples, not universal defaults. No DNS lookup is performed. IPv6, UDP/TFTP, automatic IP configuration, raw Ethernet discovery and USB serial connections are not implemented. Modern Windows with .NET Framework 4.x is the tested host; this does not establish XP/98/95 compatibility.

Verified with simulated address/subnet inputs and a temporary localhost TCP listener: accepted/refused ports, no application payload, cancellation before connecting, and local adapter/cache reading. Firmware inspection regression tests also passed. The sandbox blocked localhost connections; socket tests passed outside it. No real router or LAN peer was probed. Older device behaviour, cable hardware and actual Ethernet routing still need device testing.

ARP reference: [Microsoft IP Helper / ARP documentation](https://learn.microsoft.com/windows/desktop/IpHlp/using-the-address-resolution-protocol).
