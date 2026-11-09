# NTSServerCLI

[![CI](https://github.com/OpenChargingCloud/NTSServerCLI/actions/workflows/ci.yml/badge.svg)](https://github.com/OpenChargingCloud/NTSServerCLI/actions/workflows/ci.yml)
[![Nightly](https://github.com/OpenChargingCloud/NTSServerCLI/actions/workflows/nightly.yml/badge.svg)](https://github.com/OpenChargingCloud/NTSServerCLI/actions/workflows/nightly.yml)

One time server for a local network: NTS-KE over TLS 1.3 and over SSH, and
NTP and NTP+NTS, with a web interface, a JSON API and a prompt, until 'quit'
or Ctrl+C.

It hands out either the clock of the machine it runs on, or the time of a
group of upstream NTS servers it follows - asked over NTS-KE (TLS 1.3) and
NTP+NTS, so that the time arrives authenticated all the way from the upstream
server to the last client in the network. The key exchange is offered over
TLS, as RFC 8915 has it, and over SSH as well - the subsystem
`ntske/1@open.charging.cloud` of the white paper "NTS Key Establishment over
SSH" - so that a private network needs no PKI for NTS: a client holds the
server to its SSH host key, and the server lets a client in by its SSH key.

It is a sibling of [PKICLI](https://github.com/OpenChargingCloud/PKICLI),
[EVCLI](https://github.com/OpenChargingCloud/EVCLI) and the others, and is
built the same way. The time server itself - NTS-KE, NTP, the cookies, the
AEAD - is [Norn](https://github.com/Vanaheimr/Norn)'s; what every program of
the family has - the sign-in, the name servers (DNS), the groups of time
servers and the certificates they are held to, the SSH server with its
command line, and the log - is
[WWCP_Node](https://github.com/OpenChargingCloud/WWCP_Node); what is the NTS
server's own is in [NTSServer](https://github.com/OpenChargingCloud/NTSServer).


### Getting it

The libraries it is built from are submodules, so they have to come along:

```
git clone --recurse-submodules https://github.com/OpenChargingCloud/NTSServerCLI.git
```

If you already cloned it without them:

```
git submodule update --init --recursive
```

**On Windows**, turn long paths on first: `git config --global core.longpaths true`.


### Building and running it

.NET 10 and Node 22 or newer. The build runs `npm ci` and `npm run build` for
the web interface itself whenever its inputs changed, and embeds it into the
assembly; `-p:SkipFrontendBuild=true` builds the rest alone.

```
dotnet build NTSServerCLI.slnx
dotnet run --project NTSServerCLI
```

or `./run.sh`, which takes the same switches. `--help` lists all of them.
Without a configuration file it hands out this machine's clock on the
standard ports - NTS-KE on TCP 4460, NTP on UDP 123, on every IPv4 address -
and serves its web interface, its JSON API and its command line over SSH on
127.0.0.1 only: http://127.0.0.1:2361/ and port 22361 (`--any` for every
address). At the first start it makes one account, `root`, and shows its
password once, on the console.

Two things stand in the way of the standard ports:

* **On Windows**, the Windows Time service holds UDP 123 while it runs. Stop
  it (`net stop w32time` as an administrator), or give NTP another port with
  `--ntp-port`.
* **On Linux**, a port below 1024 takes `CAP_NET_BIND_SERVICE`:
  `sudo setcap cap_net_bind_service=+ep $(readlink -f $(which dotnet))`, or a
  systemd unit with `AmbientCapabilities=CAP_NET_BIND_SERVICE`.

Every switch of the time server says for one start what the `ntsServer`
section of the configuration file says for every start:

| switch | |
|---|---|
| `--local` | hand out this machine's clock |
| `--upstream [<group>]` | hand out the time of an NTS group of the node, `legal` by default |
| `--ntske-port <n>` | the TCP port of NTS-KE over TLS (4460) |
| `--ntp-port <n>` | the UDP port of NTP (123) |
| `--listen <address>` | where NTS-KE and NTP listen (0.0.0.0; `any` for IPv6 as well) |
| `--hostname <name>` | the name clients reach this server by |
| `--no-ntske-ssh` | no NTS-KE over SSH |


### The configuration

`configuration.json` below the repository root, or what `--config` names. The
section of the time server, with every key and its default:

```json
{
  "ntsServer": {
    "listen":     "0.0.0.0",
    "ntsKEPort":  4460,
    "ntpPort":    123,
    "hostname":   "time.lan",
    "ntpServer":  null,
    "timeSource": "local",
    "local":      { "stratum": 1, "referenceId": "LOCL", "rootDispersionMs": null },
    "upstream":   { "group": "legal", "pollSeconds": 64, "stepThresholdMs": 128,
                    "maxSlewPPM": 500, "holdoverMinutes": 60 },
    "rateLimit":  false,
    "ssh":        { "enabled": true,
                    "clients": [ { "name": "cs001", "keys": [ "ssh-ed25519 AAAA... cs001" ], "enabled": true } ] }
  }
}
```

A key nobody knows, or a value that cannot be followed, stops the start with
what is wrong - a typo in `timeSource` would otherwise be a server that
quietly hands out the local clock while somebody believes it follows the PTB.

* `hostname` is what the self-signed TLS identity of NTS-KE is made out to,
  beside the machine's own name and `localhost`.
* `ntpServer` - `"host"` or `"host:port"` - is the NTP server the key exchange
  names to its clients (RFC 8915 § 4.1.7/4.1.8). Without it nothing is named
  and a client asks the host it made the key exchange with, on port 123; with
  an NTP port other than 123 and a `hostname`, that host and port are named.
* `local` is what the answers say of the clock in local mode. Stratum 1 and
  `LOCL` are Norn's defaults; a machine whose own clock is kept by an NTP
  client of the operating system from a stratum-n server is honestly
  stratum n+1.
* `rateLimit` answers a client that asks too often with a RATE kiss-o'-death
  (RFC 8633 § 5.4), at Norn's defaults.


### Where the time comes from

**Local mode** hands out the clock of this machine, exactly as the operating
system keeps it - which is right where something else keeps that clock well:
a GPS receiver, chrony, the Windows Time service.

**Upstream mode** follows one of the node's NTS groups, the `nts` section
every program of this family has. Without one it is the PTB's four, of which
two have to answer:

```json
"nts": { "groups": {
  "legal": { "servers": [ "ptbtime1.ptb.de", "ptbtime2.ptb.de",
                          "ptbtime3.ptb.de", "ptbtime4.ptb.de" ],
             "minServers": 2 },
  "upstream": { "servers": [ "time.cloudflare.com", "nts.netnod.se" ], "minServers": 1 }
} }
```

`"upstream": { "group": "upstream" }` in the `ntsServer` section follows the
second one instead. The servers, their ports, the certificates they are held
to (`certificateFingerprints`, `rootFingerprints`, `trustOnFirstUse`) and
everything else about them are written there, as every node writes its time
servers - see the README of WWCP_Node. Their key exchange is TLS 1.3, held to
the roots the machine trusts or to a TLS root of the node's own store for
`nts`, which is how an upstream server with a self-signed certificate is
believed.

**This machine's clock is never set.** The time handed out runs on the
machine's stopwatch - unaffected by whatever else sets the wall clock - and a
correction on top of it:

* every poll interval the group is asked, band by band, and the median of the
  authenticated answers is taken once the quorum has answered: a single server
  that lies cannot move the clock;
* the first time is **stepped** to, before anything is handed out - nothing is
  answered at all, NTS-KE included, before the group has given a time;
* from then on every difference is **slewed** away by the next round, never
  faster than 500 ppm, so that time handed out never jumps and never runs
  backwards; the rate of the machine's oscillator is estimated from the last
  sixteen rounds;
* a round further off than 128 ms is passed over as an outlier; three in a row
  are the truth, and are stepped to, with a warning in the log;
* without a usable round for longer than the holdover (one hour), the time is
  no longer handed out as synchronized: nothing is answered until the group
  gives a time again.

The answers say stratum one below the upstream server closest to the median,
name it by its address as the reference (RFC 5905 § 7.3), and add its root
delay and dispersion to what the way there and the measuring add.

The node's own check of the clock against its legal group - every 15 minutes,
in the log and in the metrological log - measures the time handed out, not
the machine's clock below it.


### NTS-KE over TLS

The certificate NTS-KE shows is a TLS server identity of the node's
certificate store where one is there - made out to the name clients reach this
server by, by the PKI next door or anybody else:

```
dotnet run --project NTSServerCLI -- --import-certificate tlsServerIdentity=time.lan.p12
```

An identity told it is for `ntske` is shown here only; one told nothing is
shown on every listener. Its key has to be an elliptic curve key.

Without one, a **self-signed** identity is made at the first start and kept in
`ntsServer/ntske-tls.pem` beside the configuration file, for three years. A
client has to be told to trust it: by its SHA-256 fingerprint, which the
banner, the log and `timeServer` show, or by the file - for chrony:

```
server time.lan iburst nts
ntstrustedcerts /etc/chrony/time.lan.pem
```

with only the certificate block of the file copied over - never the private
key below it.

The master keys the cookies are sealed with are kept beside it, in
`ntsServer/masterKeys.json`: a new one every day, and those of the last seven
days as well, so that the cookies a client holds outlive a restart of the
program and the server made anew when, in upstream mode, the description of
the clock changes. Whoever reads them can open every cookie, and with it the
keys its client authenticates time with - so the directory and the file are
the owner's alone, and a key is gone from the file once it is forgotten.


### NTS-KE over SSH

The subsystem `ntske/1@open.charging.cloud` of the node's SSH server - the
same server as the command line, on 22361 by default. It listens where the
web interface listens, which is 127.0.0.1 unless the start says `--any`: for
NTS-KE over SSH in the local network, start with `--any` - and think of
`--https` for the JSON API, which then is reachable there too. Two kinds of
login may use it:

* **NTS clients** of the `ntsServer` section: a machine signs in under its name
  with one of its keys, and the node confines it to the key exchange - no
  terminal, no command line, no SFTP. Each key is an `authorized_keys` line, so
  that `from="192.168.1.0/24"` and `expiry-time="20271231"` in front of it hold
  as OpenSSH holds them. At the prompt:

  ```
  ntsClients add cs001 ssh-ed25519 AAAA... cs001
  ntsClients remove cs001 [<fingerprint>]
  ntsClients disable cs001
  ntsClients ssh off
  ```

* **Accounts** of the node with a role that may run `ntske` - the
  administrators, and the role `ntsclient`, which may do nothing else.

A client holds the server to its SSH host key, which the banner shows. The
key exchange runs as over TLS; NTP afterwards is the same, and the NTP server
cannot tell over which channel the keys were agreed. OpenSSH's `ssh -s` with
the subsystem name speaks it as well.


### The prompt

Beside every node's commands - `syncNTS` and `listNTSGroups` among them, which
ask and show the upstream servers - the NTS server has:

* `timeServer` - what it listens on, where its time comes from, how far that
  was from the upstream servers, and how much it answered;
* `timeServer sync` - ask the upstream servers now;
* `ntsClients ...` - the machines that may make key exchanges over SSH.

The prompt is the console's, and every account's over SSH:
`ssh -p 22361 root@127.0.0.1`.


### The web interface

| | |
|---|---|
| **Time server** (`/ntsServer`) | whether it answers and since when; what it hands out - this machine's clock, or the time of an NTS group on a clock of its own, with its correction and frequency - and what the answers say of it: stratum, reference, root delay and dispersion, leap indicator; in upstream mode the last round - synchronized since, the server closest to the median, how many answered, offset, the error being slewed, jitter and spread, rounds, outliers and steps - and **Ask them now**; the certificate NTS-KE over TLS shows, with its SHA-256 fingerprint and its PEM to copy or download for a client to trust; where NTP answers and what the key exchange names; NTS-KE over SSH in a few lines; and how much it answered |
| **NTS clients** (`/ntsServer/clients`) | NTS-KE over SSH - whether it is served, where, its subsystem and the host key a client holds the server to - switched on and off; the machines that may make key exchanges over SSH, each key with its fingerprint, its comment and its options; a client added with its first key, another key of one added, a key removed, a client switched off and on, and removed |
| **Configuration** | what the NTS server is running and its `ntsServer` section as it is in effect, and below it the **DNS client**, the **Upstream time servers** (the node's NTS groups), the **SSH server**, the **Trusted certificates**, **Identities** and **Server identities** of the node itself - the TLS identity NTS-KE shows among them |
| **Logs** | the log as it happens |

Every change on the NTS clients page is in effect at once and written into
the `ntsServer` section of the configuration file: a client switched off or
removed makes no key exchange from then on, and a session of it that is open
ends. Whoever may only read sees everything and changes nothing; **Ask them
now** takes a role that may run `nts`, a change of the NTS clients one that
may edit `timeserver`.

`--frontend libs/NTSServer/NTSServer/Frontend/dist` serves the web interface
from the directory instead of the assembly, for `npm run watch` there.


### The JSON API

Every node's below `/api/v1`, and the time server's:

| | |
|---|---|
| `GET /api/v1/ntsServer` | how the time server stands, the certificate of NTS-KE in PEM among it |
| `GET /api/v1/ntsServer/settings` | the `ntsServer` section as it is in effect |
| `POST /api/v1/ntsServer/sync` | ask the upstream servers now |
| `GET /api/v1/ntsServer/ssh` | NTS-KE over SSH and the NTS clients, each key with its fingerprint |
| `PUT /api/v1/ntsServer/ssh` | `{ "enabled", "clients": [ { "name", "keys": [ <authorized_keys line> ], "enabled" } ] }` - what it changes, and nothing else; answered as `GET` |


### Who may do what

Two resources beside the node's: `timeserver` - read, how the time server
stands; edited, its NTS clients and NTS-KE over SSH - and `ntske` - run, a key
exchange over SSH by an account. Two roles beside the node's viewer and
administrators: `operator`, who reads everything and asks the name and time
servers, and `ntsclient`, who makes key exchanges and nothing else.


### Where things are

| | |
|---|---|
| `NTSServerCLI/` | the command line: the time server's own switches, and its lines of what the console says at a start |
| `NTSServerCLI/CLI/` | what can be typed at the running server - `timeServer` and `ntsClients` |
| `libs/NTSServer/` | the NTS server itself - its settings, the clock it hands out, Norn's server around it, NTS-KE over SSH, its JSON API - and its tests |
| `libs/NTSServer/NTSServer/Frontend/` | the web interface: TypeScript and SCSS, bundled by webpack |
| `libs/WWCP_Node/` | what every one of these programs shares: the configuration file, the log, name resolution and the time, the certificate store, who may sign in, the command line over SSH, and the switches, the usage and the banner every node has |
| `libs/Norn/` | NTS and NTP: the key exchange over TLS and over SSH, the cookies, the AEAD, the server and the client |
| `libs/Hermod/` | the network below all of them: HTTP, TLS, SSH and DNS |
| `libs/Styx/`, `libs/Urdr/` | the basics below that, and time stamps (RFC 3161), which WWCP_Node takes from Urdr |
| `.github/workflows/` | what runs on every push, and what runs at night |

The command line is this program's vocabulary and nothing else - the switches
it is started with and the commands it can be typed at. What a time server
*is*, and what it does, lives in `libs/NTSServer`.


### Its tests

```
dotnet test libs/NTSServer/NTSServerTests/NTSServerTests.csproj
```

The settings, the clock and how it is slewed, NTS-KE over TLS and over SSH
followed by NTP+NTS - asked by Norn's own client - cookies that outlive a
restart, a second server following the first upstream, and WWCP_Node's
conformance suite asked of an NTS server, its web interface among it. Nothing
in them leaves the machine. The web interface's own tests - its pages drawn
against a stand-in server, and the rules every page of every kind of node is
held to - are run by node, in `libs/NTSServer/NTSServer/Frontend`:

```
npm run typecheck
npm run typecheck:test
npm test
```

**CI** runs them on every push, beside the node's own tests, on Windows and on
Debian 13; on Debian it then starts the server and asks it: `/` must be the
embedded web interface, the JSON API must be shut to anybody not signed in,
SSH must answer, NTS-KE must agree to TLS 1.3 with the ALPN `ntske/1`, and NTP
must answer at stratum 1. **Nightly** runs
every test of every library at the commits pinned here - but for those that
ask real name and time servers on the Internet - and builds and tests once
more with every library at its newest commit, to say what advancing them
would cost.


### What is not here yet

* The leap indicator of the upstream servers is not passed on.
* The settings of the time server itself - its ports, its time source, the
  upstream group - are changed in the configuration file, not in the web
  interface, and take effect at the next start.


## Your participation

This software is Open Source under the **Affero GPL 3.0 license**.
We appreciate your participation in this ongoing project, and your help to
improve it and the e-mobility ICT in general. If you find bugs, want to
request a feature or send us a pull request, feel free to use the normal
GitHub features to do so. For this please read the Contributor License
Agreement carefully and send us a signed copy or use a similar free and
open license.
