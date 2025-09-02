# AKS Reader Emulator ![Build](https://github.com/cancakar35/AksReaderEmulator/actions/workflows/ci.yml/badge.svg) [![Latest Release](https://img.shields.io/github/v/release/cancakar35/AksReaderEmulator?logo=github&label=Version)](https://github.com/cancakar35/AksReaderEmulator/releases)

Simulate AKS Elektronik mifare and proximity access control devices (ACS-403, ACS-451, ACS-503, ACS-551, ACS-552 etc.) for tests.

## Quick Start

- Download the latest version from the [Releases](https://github.com/cancakar35/AksReaderEmulator/releases) section.

- Run the application from your terminal
```bash
.\AksReaderEmulator.exe
```

### Arguments
- `--help`
  Displays help text.
  
- `--ip`
  Sets the ip address to listen. The default is all network interfaces (0.0.0.0)
  
- `--port`
  Sets the port to listen. The default is 1001.

- `--readerId`
  Sets the Reader Id. Maximum allowed value is 254. The default is 150.

- `--randomCardReads`
  Simulates random card reads. Allowed values are true and false. Default is false.

- `--logRequests`
  Enable request logging. If enabled, each command and parameters will be printed to the console. Allowed values are true and false. Default is false.

- `--workType`
  Sets the device work type (online,offline,onoff). Default is on_off.

- `--protocol`
  Sets the device protocol (server,client). Default is client.

<br />

**It's also possible to set the arguments via environment variables using `AKSREADER_` prefix. (e.g. AKSREADER_ip)**

## Using with Docker

- Clone the repository.
- Build container using Dockerfile provided in the project directory.
  
