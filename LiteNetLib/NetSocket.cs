#if !WINRT || UNITY_EDITOR
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace LiteNetLib
{
    internal sealed class NetSocket
    {
        private const int BufferSize = ushort.MaxValue;
        private Socket _udpSocketv4;
        private Socket _udpSocketv6;
        private NetEndPoint _localEndPoint;
        private const int SocketTTL = 255;
        private Thread _threadv4;
        private Thread _threadv6;
        private readonly NetBase.OnMessageReceived _onMessageReceived;
        private bool _running;

        public NetEndPoint LocalEndPoint
        {
            get { return _localEndPoint; }
        }

        public NetSocket(NetBase.OnMessageReceived onMessageReceived)
        {
            _onMessageReceived = onMessageReceived;
        }

        private void ReceiveLogic(object state)
        {
            Socket socket = (Socket)state;
            EndPoint bufferEndPoint = new IPEndPoint(socket.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0);
            NetEndPoint bufferNetEndPoint = new NetEndPoint((IPEndPoint)bufferEndPoint);
            byte[] receiveBuffer = new byte[NetConstants.PacketSizeLimit];

            while (_running)
            {
                //wait for data
                if (!socket.Poll(100000, SelectMode.SelectRead))
                {
                    continue;
                }

                int result;

                //Reading data
                try
                {
                    result = socket.ReceiveFrom(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, ref bufferEndPoint);
                    if (!bufferNetEndPoint.EndPoint.Equals(bufferEndPoint))
                    {
                        bufferNetEndPoint = new NetEndPoint((IPEndPoint)bufferEndPoint);
                    }
                }
                catch (SocketException ex)
                {
                    NetUtils.DebugWriteError("[R]Error code: {0} - {1}", (int)ex.SocketErrorCode, ex.ToString());
                    _onMessageReceived(null, 0, (int)ex.SocketErrorCode, bufferNetEndPoint);
                    continue;
                }

                //All ok!
                NetUtils.DebugWrite(ConsoleColor.Blue, "[R]Recieved data from {0}, result: {1}", bufferNetEndPoint.ToString(), result);
                _onMessageReceived(receiveBuffer, result, 0, bufferNetEndPoint);
            }
        }

        public bool Bind(int port)
        {
            _udpSocketv4 = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _udpSocketv4.Blocking = false;
            _udpSocketv4.ReceiveBufferSize = BufferSize;
            _udpSocketv4.SendBufferSize = BufferSize;
            _udpSocketv4.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, SocketTTL);
            _udpSocketv4.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.DontFragment, true);
            if (!BindSocket(_udpSocketv4, new IPEndPoint(IPAddress.Any, port)))
            {
                return false;
            }
            _localEndPoint = new NetEndPoint((IPEndPoint)_udpSocketv4.LocalEndPoint);

            _running = true;
            _threadv4 = new Thread(ReceiveLogic);
            _threadv4.IsBackground = true;
            _threadv4.Start(_udpSocketv4);

            //Use one port for two sockets
            if (port == 0)
                port = _localEndPoint.Port;

            _udpSocketv6 = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
            _udpSocketv6.Blocking = false;
            _udpSocketv6.ReceiveBufferSize = BufferSize;
            _udpSocketv6.SendBufferSize = BufferSize;
            if(BindSocket(_udpSocketv6, new IPEndPoint(IPAddress.IPv6Any, port)))
            {
                _localEndPoint = new NetEndPoint((IPEndPoint)_udpSocketv6.LocalEndPoint);
                _threadv6 = new Thread(ReceiveLogic);
                _threadv6.IsBackground = true;
                _threadv6.Start(_udpSocketv6);
            }

            return true;
        }

        private bool BindSocket(Socket socket, IPEndPoint ep)
        {
            try
            {
                socket.Bind(ep);
                NetUtils.DebugWrite(ConsoleColor.Blue, "[B]Succesfully binded to port: {0}", ((IPEndPoint)socket.LocalEndPoint).Port);
            }
            catch (SocketException ex)
            {
                NetUtils.DebugWriteError("[B]Bind exception: {0}", ex.ToString());
                //TODO: very temporary hack for iOS (Unity3D)
                if (ex.ErrorCode == 10047)
                {
                    return true;
                }
                return false;
            }
            return true;
        }

        public int SendTo(byte[] data, int offset, int size, NetEndPoint remoteEndPoint, ref int errorCode)
        {
            try
            {
                int result;
                if (remoteEndPoint.EndPoint.AddressFamily == AddressFamily.InterNetwork)
                {
                    if (!_udpSocketv4.Poll(5000, SelectMode.SelectWrite))
                        return -1;
                    result = _udpSocketv4.SendTo(data, offset, size, SocketFlags.None, remoteEndPoint.EndPoint);
                }
                else
                {
                    if (!_udpSocketv6.Poll(5000, SelectMode.SelectWrite))
                        return -1;
                    result = _udpSocketv6.SendTo(data, offset, size, SocketFlags.None, remoteEndPoint.EndPoint);
                }

                NetUtils.DebugWrite(ConsoleColor.Blue, "[S]Send packet to {0}, result: {1}", remoteEndPoint.EndPoint, result);
                return result;
            }
            catch (SocketException ex)
            {
                NetUtils.DebugWriteError("[S]" + ex);
                errorCode = ex.ErrorCode;
                return -1;
            }
            catch (Exception ex)
            {
                NetUtils.DebugWriteError("[S]" + ex);
                return -1;
            }
        }

        public void Close()
        {
            _running = false;
            if (Thread.CurrentThread != _threadv4)
            {
                _threadv4.Join();
            }
            _threadv4 = null;
            if (Thread.CurrentThread != _threadv6)
            {
                _threadv6.Join();
            }
            _threadv6 = null;
            if (_udpSocketv4 != null)
            {
                _udpSocketv4.Close();
                _udpSocketv4 = null;
            }
            if (_udpSocketv6 != null)
            {
                _udpSocketv6.Close();
                _udpSocketv6 = null;
            }
        }
    }
}

#endif
