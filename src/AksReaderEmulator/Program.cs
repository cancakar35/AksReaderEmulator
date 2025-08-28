using AksReaderEmulator;
using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;


var deviceCommandHandler = new DeviceCommandHandler();
using var server = new TcpListener(IPAddress.Parse("127.0.0.1"), 1001);

server.Start();

List<DevicePerson> devicePersons = [];
List<DeviceAttendance> deviceAttendances = [];
int lastReadAttendanceRecord = 0;

byte[] okCommand = deviceCommandHandler.CreateCommand(Encoding.UTF8.GetBytes("o"));
byte[] errCommand = deviceCommandHandler.CreateCommand(Encoding.UTF8.GetBytes("h"));
byte[] paramErrorResp = deviceCommandHandler.CreateCommand(Encoding.UTF8.GetBytes("n"));
byte[] emptyCardResponse = deviceCommandHandler.CreateCommand(Encoding.UTF8.GetBytes("a00"));
byte[] filledCardResponse = deviceCommandHandler.CreateCommand(Encoding.UTF8.GetBytes("b00D32EF4CF"));

while (true)
{
    byte[] buffer = ArrayPool<byte>.Shared.Rent(1024);

    try
    {
        using TcpClient client = server.AcceptTcpClient();

        NetworkStream stream = client.GetStream();

        int i;

        while ((i = stream.Read(buffer)) != 0)
        {
            byte[]? dataPart = deviceCommandHandler.GetDataPart(buffer.ToArray());
            if (dataPart == null) continue;

            int commandId = dataPart[0];
            string commandParams = Encoding.UTF8.GetString(dataPart[1..]);

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
                if (Random.Shared.Next(0, 100) > 50)
                    stream.Write(emptyCardResponse);
                else
                    stream.Write(filledCardResponse);
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
                stream.Write(getDateResponse);
            }
            else if (commandId == 24)
            {
                if (commandParams == "1" || commandParams == "2" || commandParams == "3")
                    stream.Write(okCommand);
                else
                    stream.Write(errCommand);
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
        Console.WriteLine(ex.Message);
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
