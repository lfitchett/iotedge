using System;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Diagnostics
{
    public class GetSocket
    {
        public static string GetSocketResponse(string server, string endpoint)
        {
            string request = $"GET {endpoint} HTTP/1.1\r\nHost: " + server +
                "\r\nConnection: Close\r\n\r\n";
            Byte[] bytesSent = Encoding.ASCII.GetBytes(request);
            Byte[] bytesReceived = new Byte[256];
            string page = "";

            // Create a socket connection with the specified server and port.
            using (Socket socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP))
            {
                socket.Connect(new UnixDomainSocketEndPoint(server));

                if (socket == null)
                    return "Connection failed";

                // Send request to the server.
                socket.Send(bytesSent, bytesSent.Length, 0);
                int bytes = 0;
                page = "";

                do
                {
                    bytes = socket.Receive(bytesReceived, bytesReceived.Length, 0);
                    page = page + Encoding.ASCII.GetString(bytesReceived, 0, bytes);
                }
                while (bytes > 0);
            }

            return page;
        }
    }
}