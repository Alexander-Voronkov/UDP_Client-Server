using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

namespace GetStreetByPostal
{
    public partial class Form1 : Form
    {
        Dictionary<string,string>streetPostal=null;
        Socket socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp);
        EndPoint ep;
        EndPoint curr;
        Mutex mutex;
        public Form1()
        {
            InitializeComponent();
            if (Mutex.TryOpenExisting("ServerMutex", out Mutex q) == false)
            {
                mutex = new Mutex(true,"ServerMutex");
                this.Text = "Server";
                streetPostal = new Dictionary<string, string>();
                Task.Run(LoadDoc);
                GenerateInterface(true);
                Task.Run(UDPListen);
            }
            else
            {
                int c = CountMutex();
                mutex = new Mutex(true, $"ClientMutex{c + 1}");
                this.Text = $"ClientMutex{c+1}";
                GenerateInterface(false);
                ep = new IPEndPoint(IPAddress.Loopback, 1000);
            }
        }

        private void GenerateInterface(bool f) // f - server !f - client
        {
            if(f)
            {
                Controls.Add(new ListBox() { Name = "ConnectedClients", Text = "ConnectedClients", Top = 100, Left = 100, Height = 300, Width = 300 }); ;
                Controls.Add(new Label() { Text = "ConnectedClients' messages", Top = 80, Left = 100}); ;
            }
            else
            {
                Controls.Add(new Label() { Text="Postal code",Top=100,Left=100});
                Controls.Add(new Label() { Text="City name",Top=150,Left=100});
                var b = new Button() { Text = "Add new city", Top = 200, Left = 200, Width = 150 };
                b.Click += AddClickClient;
                Controls.Add(b);
                var b1 = new Button() { Text="Get cities with this postal code", Width=200,Top=100,Left=400 };
                b1.Click += GetCitiesClickClient;
                Controls.Add(b1);
                Controls.Add(new TextBox() { Top = 100, Left = 250, Name = "Postal", Width = 100 });
                Controls.Add(new TextBox() { Top = 150, Left = 250, Name = "CityName", Width = 200 });
            }
        }

        private void GetCitiesClickClient(object sender,EventArgs ea)
        {
            if ((this.Controls.Cast<Control>().Where(x => x.Name == "Postal").ToList()[0] as TextBox).TextLength == 0) return;

            string postal = (this.Controls.Cast<Control>().Where(x => x.Name == "Postal").ToList()[0] as TextBox).Text;
            byte[] buff = Encoding.UTF8.GetBytes(postal);
            socket.BeginSendTo(buff, 0, buff.Length, SocketFlags.None, ep, CallBack, socket);
        }

        private void CallBack(IAsyncResult iar)
        {
            var client = iar.AsyncState as Socket;

            try
            {
                client.EndSendTo(iar);
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
                return;
            }
            byte[] buff = new byte[256];
            int bytes;
            var builder = new StringBuilder();
            do
            {
                bytes = client.ReceiveFrom(buff,0,buff.Length,SocketFlags.None,ref ep);
                builder.Append(Encoding.UTF8.GetString(buff,0,bytes));
            } while (client.Available>0);
            MessageBox.Show(builder.ToString());
        }

        private void CallBack1(IAsyncResult iar)
        {
            var client = iar.AsyncState as Socket;

            client.EndSendTo(iar);

            byte[] buff = new byte[256];
            int bytes;
            var builder = new StringBuilder();

            do
            {
                bytes = client.ReceiveFrom(buff, 0, buff.Length, SocketFlags.None,ref ep);
                builder.Append(Encoding.UTF8.GetString(buff,0,bytes));
            } while (client.Available > 0);

            MessageBox.Show(builder.ToString());
        }

        private void AddClickClient(object sender,EventArgs ea)
        {
            if ((this.Controls.Cast<Control>().Where(x => x.Name == "Postal").ToList()[0] as TextBox).TextLength == 0
                || (this.Controls.Cast<Control>().Where(x => x.Name == "CityName").ToList()[0] as TextBox).TextLength == 0)
                return;
            byte[] buff = Encoding.UTF8.GetBytes($"{(this.Controls.Cast<Control>().Where(x => x.Name == "CityName").ToList()[0] as TextBox).Text}:{(this.Controls.Cast<Control>().Where(x => x.Name == "Postal").ToList()[0] as TextBox).Text}");
            socket.BeginSendTo(buff, 0, buff.Length, SocketFlags.None, ep, CallBack1, socket);
        }

        private void UDPListen()
        {
            ep = new IPEndPoint(IPAddress.Loopback, 1000);
            socket.Bind(ep);
            while (true)
            {
                curr = new IPEndPoint(IPAddress.Any, 0);
                byte[] buff = new byte[256];
                int bytes;
                var builder = new StringBuilder();
                do
                {
                    bytes=socket.ReceiveFrom(buff, 0, buff.Length, SocketFlags.None, ref curr);
                    builder.Append(Encoding.UTF8.GetString(buff,0,bytes));
                } while (socket.Available > 0);
                if (InvokeRequired)
                    Invoke(new Action(() => (this.Controls.Cast<Control>().Where(x => x.Name == "ConnectedClients").ToList()[0] as ListBox).Items.Add($"{(curr as IPEndPoint).Address}:{(curr as IPEndPoint).Port} - {builder}")));
                else
                    (this.Controls.Cast<Control>().Where(x => x.Name == "ConnectedClients").ToList()[0] as ListBox).Items.Add($"{(curr as IPEndPoint).Address}:{(curr as IPEndPoint).Port} - {builder}");
                bool f = false;
                builder.ToString().ToList().ForEach(new Action<char>((x) => { if (char.IsLetter(x)) f = true; }));
                if(f)
                {
                    var s=builder.ToString().Split(':');
                    string res;
                    if (streetPostal.Contains(new KeyValuePair<string, string>(s[0], s[1])))
                    {
                        res = "already contains such city and postal";
                    }
                    else
                    {
                        streetPostal.Add(s[0], s[1]);
                        res = "successfully added!";
                        Task.Run(SaveChanges);
                    }
                    socket.SendTo(Encoding.UTF8.GetBytes(res),curr);
                }
                else
                {
                    var qq = new StringBuilder();
                    foreach (var item in streetPostal)
                    {
                        if (item.Value == builder.ToString())
                            qq.Append(item.Key+"\n");
                    }
                    if (qq.ToString().Length == 0) continue;
                    byte[] buff1 = Encoding.UTF8.GetBytes(qq.ToString());
                    socket.SendTo(buff1,0,buff1.Length,SocketFlags.None,curr);
                }
            }
        }

        private void SaveChanges()
        {
            var xml = new XDocument(new XDeclaration("1.0","utf-8","yes"),new XElement("streets"));
            foreach (var item in streetPostal)
            {
                xml.Root.Add(new XElement("street", new XAttribute("postal", item.Value)) { Value=item.Key});
            }
            xml.Save("streets.xml");
        }

        private int CountMutex()
        {
            int count = 0;
            while (Mutex.TryOpenExisting($"ClientMutex{count + 1}",out Mutex m) != false)
                count++;
            return count;
        }

        private void LoadDoc()
        {
            XmlDocument doc = new XmlDocument();
            doc.Load("streets.xml");
            if (!doc.HasChildNodes||doc.DocumentElement==null)
                return;
            foreach (XmlNode item in doc.DocumentElement.ChildNodes)
            {
                streetPostal.Add(item.InnerText, item.Attributes[0].Value);
            }
        }
    }
}
