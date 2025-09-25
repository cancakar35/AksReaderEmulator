// Licensed under the MIT License.
// https://opensource.org/license/MIT

using AksReaderEmulator;
using Microsoft.Extensions.Configuration;
using System.Buffers;
using System.Globalization;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;

Console.Title = "AKS Reader Emulator";
Console.WriteLine("© 2025 Can Çakar - Licensed under the MIT License. (https://github.com/cancakar35/AksReaderEmulator)");
Console.WriteLine();

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase)) { 
    Console.WriteLine("Usage: AksReaderEmulator [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --ip <ip_address>              IP address to listen (default: 0.0.0.0)");
    Console.WriteLine("  --port <port_number>           TCP port number to listen (default: 1001)");
    Console.WriteLine("  --readerId <id>                Reader ID (default: 150)");
    Console.WriteLine("  --randomCardReads <true|false> Enable random card reads (default: false)");
    Console.WriteLine("  --logRequests <true|false>     Enable logging of incoming requests (default: false)");
    Console.WriteLine("  --workType <1|2|3>             Device work type: 1 (online), 2 (offline), 3 (OnOff) (default: 3)");
    Console.WriteLine("  --protocol <0|1>               Device protocol: 0 (Client), 1 (Server) (default: 0)");
    Console.WriteLine();
    return;
}

IConfiguration configuration = new ConfigurationBuilder()
    .AddCommandLine(args)
    .AddEnvironmentVariables(prefix: "AKSREADER_")
    .Build();

IPAddress deviceIp = configuration["ip"] is not null ? IPAddress.Parse(configuration["ip"]!) : IPAddress.Any;
int devicePort = Convert.ToInt32(configuration["port"] ?? "1001");

ArgumentOutOfRangeException.ThrowIfNegative(devicePort);
ArgumentOutOfRangeException.ThrowIfGreaterThan(devicePort, 65535);

byte readerId = Convert.ToByte(configuration["readerId"] ?? "150");
bool withRandomCardReads = configuration["randomCardReads"] == "true";
bool withRequestCommandLogging = configuration["logRequests"] == "true";
int deviceWorkType = Convert.ToInt32(configuration["workType"] ?? "3"); // (1 : online, 2 : offline, 3 : OnOff)
int deviceProtocol = Convert.ToInt32(configuration["protocol"] ?? "0"); // (0: Client, 1: Server)

if (deviceWorkType != 1 && deviceWorkType != 2 && deviceWorkType != 3)
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"Incorrect work type: {deviceWorkType}. Allowed values: 1,2,3. Program will continue with 3 (OnOff mode)");
    deviceWorkType = 3;
    Console.ResetColor();
}
if (deviceProtocol != 0 && deviceProtocol != 1)
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"Incorrect protocol: {deviceProtocol}. Allowed values: 0,1. Program will continue with 0 (Client mode)");
    deviceProtocol = 0;
    Console.ResetColor();
}

var deviceCommandHandler = new DeviceCommandHandler(readerId);

byte[] okCommand = deviceCommandHandler.CreateCommand("o"u8);
byte[] errCommand = deviceCommandHandler.CreateCommand("h"u8);
byte[] paramErrorResp = deviceCommandHandler.CreateCommand("n"u8);
byte[] emptyCardResponse = deviceCommandHandler.CreateCommand("a00"u8);
byte[] filledCardResponse = deviceCommandHandler.CreateCommand("b00D32EF4CF"u8);

using var server = new TcpListener(deviceIp, devicePort);

server.Start();

Console.WriteLine($"Listening on {deviceIp}:{devicePort} with ReaderId={readerId}");
Console.Title = $"AKS Reader Emulator - {deviceIp}:{devicePort} (ReaderId={readerId})";

List<DevicePerson> devicePersons = [];
List<DeviceAttendance> deviceAttendances = [];
int lastReadAttendanceRecord = 0;


while (true)
{
    try
    {
        using TcpClient client = await server.AcceptTcpClientAsync();

        Console.WriteLine($"Connection with client {(client.Client.RemoteEndPoint as IPEndPoint)?.Address}");

        NetworkStream stream = client.GetStream();
        var pipeReader = PipeReader.Create(stream);
        var pipeWriter = PipeWriter.Create(stream);

        while (true)
        {
            ReadResult result = await pipeReader.ReadAsync();
            ReadOnlySequence<byte> pipeBuffer = result.Buffer;


            while (TryReadCommand(ref pipeBuffer, out byte[]? command))
            {
                if (command == null)
                    continue;

                if (!deviceCommandHandler.ValidateCommand(command))
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("Invalid request. Check message length or bcc");
                    Console.ResetColor();
                    continue;
                }
                byte[]? dataPart = deviceCommandHandler.GetDataPart(command);
                if (dataPart == null) continue;

                int commandId = dataPart[0];
                string commandParams = Encoding.UTF8.GetString(dataPart[1..]);

                if (withRequestCommandLogging)
                    Console.WriteLine($"{commandId} {commandParams}");

                if (commandId == 10)
                {
                    await pipeWriter.WriteAsync(okCommand);
                }
                else if (commandId == 11)
                {
                    if (deviceAttendances.Count > lastReadAttendanceRecord)
                    {
                        DeviceAttendance offlineAttendance = deviceAttendances[lastReadAttendanceRecord];
                        StringBuilder logRespBuilder = new("d00");
                        logRespBuilder.Append(offlineAttendance.Date.ToString("HHmmss"));
                        logRespBuilder.Append('0');
                        logRespBuilder.Append((offlineAttendance.Date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)offlineAttendance.Date.DayOfWeek));
                        logRespBuilder.Append(offlineAttendance.Date.ToString("ddMMyy"));
                        logRespBuilder.Append(offlineAttendance.CardId);
                        logRespBuilder.Append("0101000001");
                        byte[] logResp = deviceCommandHandler.CreateCommand(Encoding.UTF8.GetBytes(logRespBuilder.ToString()));
                        await pipeWriter.WriteAsync(logResp);
                        continue;
                    }
                    if (deviceWorkType == 2)
                    {
                        await pipeWriter.WriteAsync(emptyCardResponse);
                        continue;
                    }
                    if (withRandomCardReads)
                    {
                        await pipeWriter.WriteAsync((Random.Shared.Next(0, 100) > 50) ? filledCardResponse : emptyCardResponse);
                    }
                    else
                    {
                        await pipeWriter.WriteAsync(emptyCardResponse);
                    }
                }
                else if (commandId == 17)
                {
                    if (commandParams.StartsWith('+'))
                    {
                        if (commandParams.Length > 14 && DateTime.TryParseExact(commandParams[1..15], "HHmmssddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime reqDate))
                        {
                            Console.WriteLine($"{reqDate:HH:mm:ss dd.MM.yyyy} Geçiş Onaylandı - {commandParams[15..]}  (beep)");
                        }
                        else
                        {
                            Console.WriteLine("Geçiş Onaylandı (beep)");
                        }
                        await pipeWriter.WriteAsync(okCommand);
                    }
                    else if (commandParams.StartsWith('-'))
                    {
                        Console.WriteLine("(Yetkisiz) (beeeeep)");
                        await pipeWriter.WriteAsync(okCommand);
                    }
                    else
                    {
                        await pipeWriter.WriteAsync(paramErrorResp);
                    }
                }
                else if (commandId == 21)
                {
                    if (DateTime.TryParseExact(commandParams.Remove(6, 2), "HHmmssddMMyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime reqDate)
                        && (reqDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)reqDate.DayOfWeek) == Convert.ToInt32(commandParams[7].ToString()))
                    {
                        await pipeWriter.WriteAsync(okCommand);
                    }
                    else
                    {
                        await pipeWriter.WriteAsync(errCommand);
                    }
                }
                else if (commandId == 22)
                {
                    DateTime newDeviceDateTime = DateTime.Now;
                    int dayOfWeek = newDeviceDateTime.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)newDeviceDateTime.DayOfWeek;
                    byte[] getDateResponse = Encoding.UTF8.GetBytes($"c{newDeviceDateTime:HHmmss}0{dayOfWeek}{newDeviceDateTime:ddMMyy}");
                    await pipeWriter.WriteAsync(deviceCommandHandler.CreateCommand(getDateResponse));
                }
                else if (commandId == 24)
                {
                    if (commandParams != "1" && commandParams != "2" && commandParams != "3")
                    {
                        await pipeWriter.WriteAsync(errCommand);
                        continue;
                    }
                    deviceWorkType = Convert.ToInt32(commandParams);
                    await pipeWriter.WriteAsync(okCommand);
                }
                else if (commandId == 31)
                {
                    if (commandParams.Length < 16 || !DateTime.TryParseExact(commandParams[10..16], "ddMMyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime reqDate))
                    {
                        await pipeWriter.WriteAsync(errCommand);
                        continue;
                    }
                    string cardIdPart = commandParams[..8];
                    string tablePart = commandParams[8..10];
                    string namePart = commandParams[16..];
                    devicePersons.Add(new DevicePerson(cardIdPart, namePart, tablePart, reqDate));
                    await pipeWriter.WriteAsync(okCommand);
                }
                else if (commandId == 32)
                {
                    devicePersons.Clear();
                    await pipeWriter.WriteAsync(okCommand);
                }
                else if (commandId == 33)
                {
                    devicePersons.RemoveAll(p => p.CardId == commandParams);
                    await pipeWriter.WriteAsync(okCommand);
                }
                else if (commandId == 52)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("Mifare card operations not supported. Use a real device. (Will return a00 for compatibility)");
                    Console.ResetColor();
                    await pipeWriter.WriteAsync(emptyCardResponse);
                }
                else if (commandId == 54 || commandId == 55 || commandId == 56
                    || commandId == 58 || commandId == 59 || commandId == 62 || commandId == 63
                    || commandId == 70 || commandId == 71)
                {
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine("Mifare card operations not supported yet. Use a real device.");
                    Console.ResetColor();
                    await pipeWriter.WriteAsync(errCommand);
                    continue;
                }
                else if (commandId == 101)
                {
                    if (commandParams != "1" && commandParams != "0")
                    {
                        await pipeWriter.WriteAsync(errCommand);
                        continue;
                    }
                    deviceProtocol = Convert.ToInt32(commandParams);
                    await pipeWriter.WriteAsync(okCommand);
                }
                else if (commandId == 248)
                {
                    // personcount
                    byte[] respBytes = deviceCommandHandler.CreateCommand(Encoding.UTF8.GetBytes($"z{devicePersons.Count.ToString().PadLeft(5, '0')}"));
                    await pipeWriter.WriteAsync(respBytes);
                }
                else if (commandId == 249)
                {
                    // logcount (total count | readCount)
                    byte[] respBytes = deviceCommandHandler.CreateCommand(Encoding.UTF8.GetBytes($"z{deviceAttendances.Count.ToString().PadLeft(10, '0')}{lastReadAttendanceRecord.ToString().PadLeft(10, '0')}"));
                    await pipeWriter.WriteAsync(respBytes);
                }
                else if (commandId == 250)
                {
                    if (commandParams == "DEL")
                    {
                        deviceAttendances.Clear();
                        lastReadAttendanceRecord = 0;
                        await pipeWriter.WriteAsync(okCommand);
                    }
                    else
                    {
                        await pipeWriter.WriteAsync(errCommand);
                    }
                }
                else if (commandId == 111)
                {
                    if (deviceAttendances.Count > lastReadAttendanceRecord)
                        lastReadAttendanceRecord++;

                    continue;
                }
            }

            pipeReader.AdvanceTo(pipeBuffer.Start, pipeBuffer.End);

            if (result.IsCompleted)
            {
                break;
            }
        }
        await pipeReader.CompleteAsync();
        await pipeWriter.CompleteAsync();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine(ex.Message);
        Console.ResetColor();
    }
}



static bool TryReadCommand(ref ReadOnlySequence<byte> buffer, out byte[]? command)
{
    command = null;

    var start = buffer.PositionOf((byte)2);
    if (!start.HasValue)
        return false;

    var end = buffer.Slice(start.Value).PositionOf((byte)3);
    if (!end.HasValue)
        return false;

    command = buffer.Slice(start.Value, buffer.GetPosition(1, end.Value)).ToArray();

    buffer = buffer.Slice(buffer.GetPosition(1, end.Value));
    return true;
}
