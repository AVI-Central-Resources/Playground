using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Crestron.SimplSharp;
using Crestron.SimplSharp.CrestronSockets;

namespace Shure_ULX_D_r1._2
{
    public delegate void SendData(UInt16 Channel, UInt16 Field);

    public class ChannelEventArgs : EventArgs
    {
        public ushort Field;
        public ushort ChannelNumber;
        public ULXD_Channel ChannelData;

        public ChannelEventArgs()
        {
        }

        public ChannelEventArgs(int Field, int ChannelNumber, ULXD_Channel Channel)
        {
            this.Field = (ushort)Field;
            this.ChannelNumber = (ushort)ChannelNumber;
            this.ChannelData = Channel;
        }
    }

    public class ULXD_Receiver
    {
        public ULXD_Channel[] Channel = new ULXD_Channel[5];

        public string IpAddress { get; set; }
        public int TcpPort { get; set; }
        public int MeteringSpeed { get; set; }

        private TCPClient TCP;

        private List<Regex> Patterns = new List<Regex>();
        private String ShureBuffer;

        public event EventHandler<ChannelEventArgs> OnChange = delegate { };

        public ULXD_Receiver()
        {
            for (int i = 0; i < Channel.Length; i++)
            {
                Channel[i] = new ULXD_Channel();
            }

            Patterns.Add(new Regex("< SAMPLE ([0-9]{1}) ALL ([A-Z]{2}) ([0-9]+) ([0-9]+) >"));
            Patterns.Add(new Regex("< REP ([0-9]{1}) AUDIO_MUTE ([A-Z]+) >"));
            Patterns.Add(new Regex("< REP ([0-9]{1}) BATT_RUN_TIME ([0-9]+) >"));
            Patterns.Add(new Regex("< REP ([0-9]{1}) TX_TYPE ([A-Z0-9]+) >"));
            Patterns.Add(new Regex("< REP ([0-9]{1}) BATT_TYPE ([A-Z0-9]+) >"));
            Patterns.Add(new Regex("< REP ([0-9]{1}) RF_INT_DET ([A-Z]+) >"));
            Patterns.Add(new Regex("< REP ([0-9]{1}) BATT_(CHARGE|BARS) ([0-9]+) >"));
        }

        public SendData UpdateSimplPlus { get; set; }

        public void ParseRx(string Rx)
        {
            int C = 0;
            Regex R;
            for (int i = 0; i < Patterns.Count; i++)
            {
                R = Patterns[i];

                Match M = R.Match(Rx);
                //CrestronConsole.PrintLine(String.Format("Checking for match against pattern[{0}]", i));
                if (M.Success)
                {
                    bool update = true;
                    C = int.Parse(M.Groups[1].ToString());
                    switch (i)
                    {
                        case 0:
                            {
                                ushort Rf = ushort.Parse(M.Groups[3].ToString());
                                short Audio = (short)(int.Parse(M.Groups[4].ToString()) - 50);

                                if ((Channel[C].RfLevel == Rf) && (Channel[C].AudioLevel == Audio))
                                    update = false;

                                Channel[C].RfLevel = Rf;
                                Channel[C].AudioLevel = Audio;
                                break;
                            }
                        case 1:
                            {
                                Channel[C].AudioMute = M.Groups[2].ToString().Equals("ON");
                                break;
                            }
                        case 2:
                            {
                                ushort Minutes = Convert.ToUInt16(M.Groups[2].ToString());
                                if (Minutes < 65535)
                                    Channel[C].BatteryMinutesRemaining = Minutes;
                                else
                                    Channel[C].BatteryMinutesRemaining = 0;
                                break;
                            }
                        case 3:
                            {
                                Channel[C].TxType = M.Groups[2].ToString();
                                break;
                            }
                        case 4:
                            {
                                Channel[C].BatteryType = M.Groups[2].ToString();
                                break;
                            }
                        case 5:
                            {
                                Channel[C].Interference = M.Groups[2].ToString().Equals("CRITICAL");
                                break;
                            }
                        case 6:
                            {
                                ushort value = Convert.ToUInt16(M.Groups[3].ToString());
                                if (value == 255)
                                    value = 0;

                                if (M.Groups[2].ToString().Equals("BARS"))
                                    Channel[C].BatteryLevel = (ushort)(value * 20);
                                else
                                    Channel[C].BatteryLevel = value;
                                break;
                            }
                        default:
                            break;
                    } // end switch
                    if (update)
                    {
                        //UpdateSimplPlus((ushort)C, (ushort)i);
                        OnChange(this, new ChannelEventArgs(i,C,Channel[C]));
                    }
                    return;
                } // end if(M.Success)
            } // end for-loop
        }

        public void SetConfig(string address, int port, int meter)
        {
            IpAddress = address;
            TcpPort = port;
            MeteringSpeed = meter;

            if (!Object.ReferenceEquals(TCP, null))
                Disconnect();

            TCP = new TCPClient(IpAddress, TcpPort, 5000);
            TCP.SocketStatusChange += new TCPClientSocketStatusChangeEventHandler(TCP_SocketStatusChange);
        }

        void TCP_SocketStatusChange(TCPClient myTCPClient, SocketStatus clientSocketStatus)
        {
            if (myTCPClient.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
                UpdateSimplPlus((ushort)1, (ushort)99);  // Field 99 is Connected_F
            else
                UpdateSimplPlus((ushort)0, (ushort)99);  // Field 99 is Connected_F
        }

        private void ConnectCallback(TCPClient Client)
        {
            Client.ReceiveDataAsync(TcpReceiveCallback);
            if (Client.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
            {
                byte[] Tx;

                Tx = System.Text.Encoding.ASCII.GetBytes(String.Format("< SET 0 METER_RATE {0} >", MeteringSpeed));
                Client.SendData(Tx, Tx.Length);

                Tx = System.Text.Encoding.ASCII.GetBytes(String.Format("< GET 0 BATT_TYPE >"));
                Client.SendData(Tx, Tx.Length);

                Tx = System.Text.Encoding.ASCII.GetBytes(String.Format("< GET 0 TX_TYPE >"));
                Client.SendData(Tx, Tx.Length);

                Tx = System.Text.Encoding.ASCII.GetBytes(String.Format("< GET 0 AUDIO_MUTE >"));
                Client.SendData(Tx, Tx.Length);

                Tx = System.Text.Encoding.ASCII.GetBytes(String.Format("< GET 0 BATT_CHARGE >"));
                Client.SendData(Tx, Tx.Length);

                Tx = System.Text.Encoding.ASCII.GetBytes(String.Format("< GET 0 BATT_RUN_TIME >"));
                Client.SendData(Tx, Tx.Length);

                Tx = System.Text.Encoding.ASCII.GetBytes(String.Format("< GET 0 BATT_BARS >"));
                Client.SendData(Tx, Tx.Length);

                Tx = System.Text.Encoding.ASCII.GetBytes(String.Format("< GET 0 RF_INT_DET >"));
                Client.SendData(Tx, Tx.Length);

            }
        }

        private void TcpReceiveCallback(TCPClient Client, int Bytes)
        {
            String RawRx = new string(Client.IncomingDataBuffer.Take(Bytes).Select(b => (char)b).ToArray());
            ShureBuffer = ShureBuffer + RawRx;

            while (ShureBuffer.IndexOf(">") >= 0)
            {
                string Rx = ShureBuffer.Substring(0, ShureBuffer.IndexOf(">") + 1);
                ShureBuffer = ShureBuffer.Remove(0, ShureBuffer.IndexOf(">") + 1);

                ParseRx(Rx);
            }
            Client.ReceiveDataAsync(TcpReceiveCallback);
        }

        public void Connect()
        {
            SocketErrorCodes Error = TCP.ConnectToServerAsync(ConnectCallback);
        }

        public void Disconnect()
        {
            if (TCP.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
            {
                byte[] Tx = System.Text.Encoding.ASCII.GetBytes(String.Format("< SET 0 METER_RATE 0 >"));
                TCP.SendData(Tx, Tx.Length);
                SocketErrorCodes Error = TCP.DisconnectFromServer();
            }
        }
    }
}