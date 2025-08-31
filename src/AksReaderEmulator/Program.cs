// Licensed under the MIT License.
// https://opensource.org/license/MIT

using AksReaderEmulator;
using Microsoft.Extensions.Configuration;
using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

Console.Title = "AKS Reader Emulator";
Console.WriteLine("© 2025 Can Çakar - Licensed under the MIT License. (https://github.com/cancakar35/AksReaderEmulator)");
Console.WriteLine();

IConfiguration configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
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
    byte[] buffer = ArrayPool<byte>.Shared.Rent(1024);

    try
    {
        using TcpClient client = server.AcceptTcpClient();

        Console.WriteLine($"Connection with client {(client.Client.RemoteEndPoint as IPEndPoint)?.Address}");

        NetworkStream stream = client.GetStream();

        int i;

        while ((i = stream.Read(buffer)) != 0)
        {
            byte[]? dataPart = deviceCommandHandler.GetDataPart(buffer);
            if (dataPart == null) continue;

            int commandId = dataPart[0];
            string commandParams = Encoding.UTF8.GetString(dataPart[1..]);

            Array.Clear(buffer);

            if (withRequestCommandLogging)
                Console.WriteLine($"{commandId} {commandParams}");

            if (commandId == 10)
            {
                stream.Write(okCommand);
            }
            else if (commandId == 11)
            {
                // TODO: 
                //if (deviceAttendances.Count > lastReadAttendanceRecord)
                //{
                //    d00 log response here
                //}
                if (deviceWorkType == 2)
                {
                    stream.Write(emptyCardResponse);
                    continue;
                }
                if (withRandomCardReads)
                {
                    stream.Write((Random.Shared.Next(0, 100) > 50) ? filledCardResponse : emptyCardResponse);
                }
                else
                {
                    stream.Write(emptyCardResponse);
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
                    stream.Write(okCommand);
                }
                else if (commandParams.StartsWith('-'))
                {
                    Console.WriteLine("(Yetkisiz) (beeeeep)");
                    stream.Write(okCommand);
                }
                else
                {
                    stream.Write(paramErrorResp);
                }
            }
            else if (commandId == 21)
            {
                if (DateTime.TryParseExact(commandParams.Remove(6, 2), "HHmmssddMMyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime reqDate)
                    && (reqDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)reqDate.DayOfWeek) == Convert.ToInt32(commandParams[7]))
                {
                    stream.Write(okCommand);
                }
                else
                {
                    stream.Write(errCommand);
                }
            }
            else if (commandId == 22)
            {
                DateTime newDeviceDateTime = DateTime.Now;
                int dayOfWeek = newDeviceDateTime.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)newDeviceDateTime.DayOfWeek;
                byte[] getDateResponse = Encoding.UTF8.GetBytes($"c{newDeviceDateTime:HHmmss}0{dayOfWeek}{newDeviceDateTime:ddMMyy}");
                stream.Write(deviceCommandHandler.CreateCommand(getDateResponse));
            }
            else if (commandId == 24)
            {
                if (commandParams != "1" && commandParams != "2" && commandParams != "3")
                {
                    stream.Write(errCommand);
                    continue;
                }
                deviceWorkType = Convert.ToInt32(commandParams);
                stream.Write(okCommand);
            }
            else if (commandId == 31)
            {
                if (commandParams.Length < 16 || !DateTime.TryParseExact(commandParams[10..16], "ddMMyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime reqDate))
                {
                    stream.Write(errCommand);
                    continue;
                }
                string cardIdPart = commandParams[..8];
                string tablePart = commandParams[8..10];
                string namePart = commandParams[16..];
                devicePersons.Add(new DevicePerson(cardIdPart, namePart, tablePart, reqDate));
                stream.Write(okCommand);
            }
            else if (commandId == 32)
            {
                devicePersons.Clear();
                stream.Write(okCommand);
            }
            else if (commandId == 33)
            {
                devicePersons.RemoveAll(p => p.CardId == commandParams);
                stream.Write(okCommand);
            }
            else if (commandId == 52)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("Mifare card operations not supported. Use a real device. (Will return a00 for compatibility)");
                Console.ResetColor();
                stream.Write(emptyCardResponse);
            }
            else if (commandId == 54 || commandId == 55 || commandId == 56
                || commandId == 58 || commandId == 59 || commandId == 62 || commandId == 63
                || commandId == 70 || commandId == 71)
            {
                throw new NotImplementedException("Mifare card operations not supported. Use a real device.");
            }
            else if (commandId == 101)
            {
                if (commandParams != "1" && commandParams != "0")
                {
                    stream.Write(errCommand);
                    continue;
                }
                deviceProtocol = Convert.ToInt32(commandParams);
                stream.Write(okCommand);
            }
            else if (commandId == 248)
            {
                // personcount
                byte[] respBytes = deviceCommandHandler.CreateCommand(Encoding.UTF8.GetBytes($"z{devicePersons.Count.ToString().PadLeft(5, '0')}"));
                stream.Write(respBytes);
            }
            else if (commandId == 249)
            {
                // logcount (total count | readCount)
                byte[] respBytes = deviceCommandHandler.CreateCommand(Encoding.UTF8.GetBytes($"z{deviceAttendances.Count.ToString().PadLeft(10, '0')}{lastReadAttendanceRecord.ToString().PadLeft(10, '0')}"));
                stream.Write(respBytes);
            }
            else if (commandId == 250 && commandParams == "DEL")
            {
                deviceAttendances.Clear();
                lastReadAttendanceRecord = 0;
                stream.Write(okCommand);
            }
            else if (commandId == 111)
            {
                if (deviceAttendances.Count > lastReadAttendanceRecord)
                    lastReadAttendanceRecord++;

                continue;
            }
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine(ex.Message);
        Console.ResetColor();
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
    }
}
