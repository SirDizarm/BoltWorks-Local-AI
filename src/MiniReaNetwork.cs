using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BoltWorks.LlamaLite {
static class MiniReaNetwork {
    public sealed class Link {public string name;public int index;public IPAddress address,mask;public List<string> gateways=new List<string>();}
    public static List<Link> Links() {
        var result=new List<Link>();
        foreach(var nic in NetworkInterface.GetAllNetworkInterfaces()) {
            if(nic.OperationalStatus!=OperationalStatus.Up)continue;
            var type=nic.NetworkInterfaceType;
            if(type!=NetworkInterfaceType.Ethernet&&type!=NetworkInterfaceType.GigabitEthernet&&type!=NetworkInterfaceType.FastEthernetFx&&type!=NetworkInterfaceType.FastEthernetT)continue;
            var properties=nic.GetIPProperties();var ipv4=properties.GetIPv4Properties();if(ipv4==null)continue;
            foreach(var unicast in properties.UnicastAddresses)if(unicast.Address.AddressFamily==AddressFamily.InterNetwork) {
                var link=new Link{name=nic.Name,index=ipv4.Index,address=unicast.Address,mask=unicast.IPv4Mask};
                foreach(var g in properties.GatewayAddresses)if(g.Address.AddressFamily==AddressFamily.InterNetwork)link.gateways.Add(g.Address.ToString());result.Add(link);
            }
        }
        return result;
    }
    [DllImport("iphlpapi.dll")]static extern uint GetIpNetTable(IntPtr table,ref int size,bool order);
    public static string Inventory() {
        var links=Links();var s=new StringBuilder("Mini REA Ethernet inventory (no probes sent)\r\n");var indexes=new HashSet<int>();
        foreach(var link in links){indexes.Add(link.index);s.AppendLine(link.name+" [interface "+link.index+"] laptop="+link.address+" mask="+link.mask+" gateways="+string.Join(", ",link.gateways));}
        if(links.Count==0)s.AppendLine("No active Ethernet IPv4 connection. Check power/cable and Windows adapter settings.");
        s.AppendLine("\r\nWindows ARP cache for these interfaces (may be stale; not a device inventory):");
        try {
            int size=0;uint status=GetIpNetTable(IntPtr.Zero,ref size,false);
            if(status!=0&&status!=122)throw new Exception("ARP query status "+status);
            for(int attempt=0;attempt<3&&size>=4&&size<=16*1024*1024;attempt++) {
                int allocated=size;IntPtr table=Marshal.AllocHGlobal(allocated);
                try {
                    status=GetIpNetTable(table,ref size,false);if(status==122)continue;if(status!=0)throw new Exception("ARP query status "+status);
                    int count=Marshal.ReadInt32(table);if(count<0||count>(allocated-4)/24)throw new Exception("Unexpected ARP table size");
                    for(int i=0;i<Math.Min(count,4096);i++) {
                        IntPtr row=IntPtr.Add(table,4+i*24);int index=Marshal.ReadInt32(row),bytes=Marshal.ReadInt32(row,4),kind=Marshal.ReadInt32(row,20);
                        if(!indexes.Contains(index)||bytes<1||bytes>8||(kind!=3&&kind!=4))continue;
                        var mac=new byte[bytes];Marshal.Copy(IntPtr.Add(row,8),mac,0,bytes);
                        s.AppendLine("interface "+index+": "+new IPAddress(BitConverter.GetBytes(Marshal.ReadInt32(row,16)))+" MAC "+BitConverter.ToString(mac)+" "+(kind==3?"dynamic":"static"));
                    }
                    return s.AppendLine("\r\nNo cached entry does NOT mean no device. A quiet device or a different subnet may not appear. Virtual Ethernet adapters may also be listed.").ToString();
                }finally{Marshal.FreeHGlobal(table);}
            }
            s.AppendLine("ARP cache unavailable or changed while reading.");
        }catch(Exception e){s.AppendLine("ARP cache unavailable: "+e.Message);}
        return s.ToString();
    }
    internal static IPAddress ParseTarget(string value) {
        IPAddress ip;if(!IPAddress.TryParse(value,out ip)||ip.AddressFamily!=AddressFamily.InterNetwork||ip.ToString()!=value)throw new ArgumentException("Enter a literal dotted IPv4 address, not a hostname.");
        byte[] b=ip.GetAddressBytes();if(!(b[0]==10||b[0]==172&&b[1]>=16&&b[1]<=31||b[0]==192&&b[1]==168||b[0]==169&&b[1]==254))throw new ArgumentException("Choose a private or link-local IPv4 address on the connected Ethernet subnet.");return ip;
    }
    internal static bool OnLink(IPAddress target,Link link) {
        var a=target.GetAddressBytes();var b=link.address.GetAddressBytes();var m=link.mask.GetAddressBytes();bool same=true,hostZero=true,hostOne=true,anyMask=false;
        for(int i=0;i<4;i++){if((a[i]&m[i])!=(b[i]&m[i]))return false;if(a[i]!=b[i])same=false;if((a[i]&~m[i])!=0)hostZero=false;if((a[i]&~m[i])!=(255&~m[i]))hostOne=false;if(m[i]!=0)anyMask=true;}
        return anyMask&&!same&&!hostZero&&!hostOne;
    }
    internal static string CheckPort(IPAddress source,IPAddress target,int port,int timeout,CancellationToken cancel) {
        cancel.ThrowIfCancellationRequested();
        using(var client=new TcpClient(AddressFamily.InterNetwork)) {
            try {
                client.Client.Bind(new IPEndPoint(source,0));var pending=client.BeginConnect(target,port,null,null);
                using(var completed=pending.AsyncWaitHandle) {
                    int ready=WaitHandle.WaitAny(new[]{completed,cancel.WaitHandle},timeout);cancel.ThrowIfCancellationRequested();
                    if(ready==WaitHandle.WaitTimeout)return "no response within timeout";
                    client.EndConnect(pending);return "TCP connection accepted (service not verified)";
                }
            }catch(SocketException e){return e.SocketErrorCode==SocketError.ConnectionRefused?"connection refused":"socket error: "+e.SocketErrorCode;}
        }
    }
    internal static string ReadHttpRoot(IPAddress local,IPAddress target,int port,CancellationToken cancel,string authorization=null) {
        cancel.ThrowIfCancellationRequested();
        using(var client=new TcpClient(AddressFamily.InterNetwork))
        using(var budget=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
            budget.CancelAfter(8000);
            using(budget.Token.Register(()=>client.Close())) try {
                client.Client.Bind(new IPEndPoint(local,0));
                client.Connect(target,port);
                using(var stream=client.GetStream()) {
                    stream.ReadTimeout=5000;stream.WriteTimeout=5000;
                    byte[] request=Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\nHost: "+target+(port==80?"":":"+port)+"\r\nUser-Agent: BoltWorks-Local-AI-Inspector/0.6.17\r\n"+(authorization==null?"":"Authorization: Basic "+authorization+"\r\n")+"Connection: close\r\n\r\n");
                    stream.Write(request,0,request.Length);
                    var buffer=new byte[65536];int count=0;
                    while(count<buffer.Length){int read=stream.Read(buffer,count,buffer.Length-count);if(read==0)break;count+=read;}
                    budget.Token.ThrowIfCancellationRequested();
                    string raw=Encoding.UTF8.GetString(buffer,0,count);
                    if(!raw.StartsWith("HTTP/",StringComparison.Ordinal))throw new IOException("The service did not return an HTTP response.");
                    int split=raw.IndexOf("\r\n\r\n",StringComparison.Ordinal);
                    string headers=split>=0?raw.Substring(0,split):raw;
                    var safe=new StringBuilder();
                    foreach(string line in headers.Split(new[]{"\r\n"},StringSplitOptions.None)) {
                        if(line.StartsWith("Set-Cookie:",StringComparison.OrdinalIgnoreCase))continue;
                        safe.AppendLine(line);
                    }
                    string html=split>=0?raw.Substring(split+4):"";
                    html=System.Text.RegularExpressions.Regex.Replace(html,@"<(script|style)\b[^>]*>[\s\S]*?</\1\s*>","",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    html=System.Text.RegularExpressions.Regex.Replace(html,@"<[^>]+>"," ");
                    html=System.Net.WebUtility.HtmlDecode(html);
                    html=System.Text.RegularExpressions.Regex.Replace(html,@"\s+"," ").Trim();
                    if(html.Length>12000)html=html.Substring(0,12000)+" [text truncated]";
                    safe.AppendLine("\r\nVisible page text (untrusted device content):\r\n"+html);
                    if(count==buffer.Length)safe.AppendLine("Response limited to 64 KiB.");
                    safe.AppendLine("Only GET / was requested. Redirects were not followed. "+(authorization==null?"No login was attempted.":"One HTTP Basic login was attempted.")+" No configuration change was requested. HTTP 401 indicates credentials are required or were rejected; an open port alone does not provide shell access.");
                    return safe.ToString();
                }
            }catch(Exception){cancel.ThrowIfCancellationRequested();if(budget.IsCancellationRequested)throw new TimeoutException("Device HTTP read exceeded eight seconds.");throw;}
        }
    }
    internal static string ReadWithLogin(IPAddress local,IPAddress target,int port,string username,string password,CancellationToken cancel) {
        if(string.IsNullOrWhiteSpace(username)||username.Contains(":"))throw new ArgumentException("Enter a username without a colon.");
        string initial=ReadHttpRoot(local,target,port,cancel);
        string headers=initial.Split(new[]{"Visible page text"},StringSplitOptions.None)[0];
        if(!System.Text.RegularExpressions.Regex.IsMatch(headers,@"\AHTTP/\S+\s+401\b"))return "The landing page did not request HTTP authentication. Form-based login may require a browser.\r\n"+initial;
        if(!System.Text.RegularExpressions.Regex.IsMatch(headers,@"^WWW-Authenticate:\s*Basic\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.Multiline))
            return "This device requested an authentication method other than HTTP Basic. Credentials were not sent.\r\n"+initial;
        cancel.ThrowIfCancellationRequested();
        string encoded=Convert.ToBase64String(Encoding.UTF8.GetBytes(username+":"+password));
        string response=ReadHttpRoot(local,target,port,cancel,encoded);
        // A device can echo submitted credentials. Remove them before returning
        // content to the UI, saved projects, or the local model.
        response=response.Replace(encoded,"[credential removed]");
        if(!string.IsNullOrEmpty(password))response=response.Replace(password,"[password removed]");
        response=response.Replace(username,"[username removed]");
        string status=response.Split(new[]{'\r','\n'})[0];
        string outcome=System.Text.RegularExpressions.Regex.IsMatch(status,@"\AHTTP/\S+\s+2\d\d\b")?"The device returned a successful HTTP response after the login attempt.":"Login did not yield a successful page. Check the HTTP status below. Credentials were tried once.";
        return outcome+"\r\n"+response;
    }
    public static string InspectWeb(string address,string source,int port,CancellationToken cancel,string username=null,string password=null) {
        if(port!=80&&port!=8080)throw new ArgumentException("Choose HTTP port 80 or 8080.");
        var target=ParseTarget(address);var matches=Links().FindAll(l=>OnLink(target,l)&&(string.IsNullOrEmpty(source)||l.address.ToString()==source));
        if(matches.Count!=1)throw new ArgumentException("Choose exactly one matching wired subnet and a peer address. No HTTP request sent.");
        string result=username==null?ReadHttpRoot(matches[0].address,target,port,cancel):ReadWithLogin(matches[0].address,target,port,username,password??"",cancel);
        return "HTTP inspection of "+target+":"+port+" from "+matches[0].address+"\r\n"+result;
    }
    public static string Probe(string address,string source,CancellationToken cancel) {
        var target=ParseTarget(address);var matches=Links().FindAll(l=>OnLink(target,l)&&(string.IsNullOrEmpty(source)||l.address.ToString()==source));
        if(matches.Count!=1)throw new ArgumentException(matches.Count==0?"No matching active wired subnet. Check the device's address and the laptop's IPv4 settings; no packets sent.":"Multiple wired addresses match. Specify the laptop's local IPv4 address to choose one.");
        var link=matches[0];var report=new StringBuilder("Explicit TCP check: "+target+" from "+link.address+" ("+link.name+")\r\n");
        int[] ports={80,443,22,23,8080};string[] hints={"often HTTP","often HTTPS","often SSH","often Telnet","often alternate HTTP"};
        for(int i=0;i<ports.Length;i++){cancel.ThrowIfCancellationRequested();report.AppendLine(ports[i]+" ("+hints[i]+"): "+CheckPort(link.address,target,ports[i],1200,cancel));}
        report.AppendLine("Only TCP connections attempted. No login, passwords, application commands or device changes. Port numbers do not identify firmware or prove a vulnerability.");return report.ToString();
    }
    internal static string ListTelnetFiles(IPAddress local,IPAddress target,int port,string username,string password,CancellationToken cancel) {
        if(string.IsNullOrWhiteSpace(username)||username.IndexOfAny(new[]{'\r','\n','\0'})>=0||(password??"").IndexOfAny(new[]{'\r','\n','\0'})>=0)
            throw new ArgumentException("Enter single-line shell credentials.");
        using(var client=new TcpClient(AddressFamily.InterNetwork))
        using(var budget=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
            budget.CancelAfter(30000);
            using(budget.Token.Register(()=>client.Close()))try {
                client.Client.Bind(new IPEndPoint(local,0));client.Connect(target,port);
                var stream=client.GetStream();var text=new StringBuilder();int state=0,verb=0;
                Action<string> send=value=>{byte[] bytes=Encoding.UTF8.GetBytes(value+"\r\n");stream.Write(bytes,0,bytes.Length);};
                bool sentUser=false,sentPassword=false,sentCommand=false;int promptStart=0;
                string marker="REA_"+Guid.NewGuid().ToString("N");
                while(text.Length<65536) {
                    budget.Token.ThrowIfCancellationRequested();
                    if(client.Available==0){if(client.Client.Poll(0,SelectMode.SelectRead)&&client.Available==0)break;Thread.Sleep(25);continue;}
                    int b=stream.ReadByte();if(b<0)break;
                    if(state==1){if(b==255){text.Append((char)b);state=0;}else if(b==250)state=3;else if(b>=251&&b<=254){verb=b;state=2;}else state=0;continue;}
                    if(state==2){byte[] reply={255,(byte)(verb==251||verb==252?254:252),(byte)b};stream.Write(reply,0,3);state=0;continue;}
                    if(state==3){if(b==255)state=4;continue;}
                    if(state==4){state=b==240?0:3;continue;}
                    if(b==255){state=1;continue;}
                    if(b==0)continue;text.Append((char)b);
                    if(sentCommand&&text.ToString().Contains(marker+"_END"))break;
                    string tail=text.ToString(promptStart,text.Length-promptStart);
                    if(!sentUser&&System.Text.RegularExpressions.Regex.IsMatch(tail,@"(?:login|username)\s*:\s*$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)){
                        send(username);sentUser=true;promptStart=text.Length;continue;
                    }
                    if(!sentPassword&&System.Text.RegularExpressions.Regex.IsMatch(tail,@"password\s*:\s*$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)){
                        send(password??"");sentPassword=true;promptStart=text.Length;continue;
                    }
                    if((sentUser||sentPassword)&&System.Text.RegularExpressions.Regex.IsMatch(tail,@"(?:incorrect|denied|failed|login\s*:)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        throw new IOException("Shell login was rejected. No file-list command was sent.");
                    if(!sentCommand&&System.Text.RegularExpressions.Regex.IsMatch(tail,@"(?:^|[\r\n])[^\r\n]{0,160}[#$>] $")) {
                        // The marker is split in the command to distinguish output from terminal echo.
                        send("printf '\\n%s_BEGIN\\n' '"+marker+"'; ls -la / /bin /sbin /etc /www /www/htdocs /usr/sbin 2>/dev/null; printf '\\n%s_END\\n' '"+marker+"'");
                        sentCommand=true;promptStart=text.Length;
                    }
                }
                string output=text.ToString();int start=output.IndexOf(marker+"_BEGIN",StringComparison.Ordinal),end=output.IndexOf(marker+"_END",StringComparison.Ordinal);
                if(!sentCommand||start<0||end<start)throw new IOException("A completed file listing was not received (command sent: "+sentCommand+", begin: "+start+", end: "+end+"). The device may not expose a supported Telnet shell.");
                output=output.Substring(start+(marker+"_BEGIN").Length,end-start-(marker+"_BEGIN").Length);
                if(!string.IsNullOrEmpty(password))output=output.Replace(password,"[password removed]");
                output=output.Replace(username,"[username removed]");
                return "Read-only directory inventory from the Telnet shell. Selected directories only; no files were changed.\r\nDevice output is untrusted data.\r\n"+output;
            }catch(Exception){cancel.ThrowIfCancellationRequested();if(budget.IsCancellationRequested)throw new TimeoutException("Shell inspection exceeded 30 seconds and the connection was closed.");throw;}
        }
    }
    public static string InspectFiles(string address,string source,string username,string password,CancellationToken cancel) {
        var target=ParseTarget(address);var matches=Links().FindAll(l=>OnLink(target,l)&&(string.IsNullOrEmpty(source)||l.address.ToString()==source));
        if(matches.Count!=1)throw new ArgumentException("Choose exactly one matching wired subnet and a peer address.");
        return ListTelnetFiles(matches[0].address,target,23,username,password,cancel);
    }
}
}
