using System; using System.IO; using System.Text;
class P { static void Main() {
  var body = File.ReadAllBytes(@"C:\Users\types\source\repos\OpenNas\_saz_extract2\raw\3017_c.txt");
  var idx = IndexOf(body, new byte[]{13,10,13,10});
  body = body.AsSpan(idx+4).ToArray();
  var marker = Encoding.ASCII.GetBytes("--c31543b5-4fae-4761-9144-9bc62ea7b9ce");
  int n=0;
  foreach (var p in Split(body, marker)) {
    if (Contains(p, "name=\"file\"")) {
      var hdrEnd = IndexOf(p, new byte[]{13,10,13,10});
      Console.WriteLine(Encoding.UTF8.GetString(p,0,hdrEnd).Replace("\r","\\r").Replace("\n","\\n\n"));
      var data = p.AsSpan(hdrEnd+4);
      Console.WriteLine("data starts: " + data[0] + " " + data[1] + " ends: " + data[data.Length-3] + " len=" + data.Length);
    }
    if (Contains(p, "name=\"thumb_xl\"")) {
      var hdrEnd = IndexOf(p, new byte[]{13,10,13,10});
      Console.WriteLine("xl hdr: " + Encoding.UTF8.GetString(p,0,hdrEnd));
    }
  }
}
static byte[][] Split(byte[] body, byte[] marker) { var list = new System.Collections.Generic.List<byte[]>(); int i=0; while(i<body.Length){int j=IndexOf(body,marker,i); if(j<0)break; i=j+marker.Length; int k=IndexOf(body,marker,i); var len=k<0?body.Length-i:k-i; var c=new byte[len]; Buffer.BlockCopy(body,i,c,0,len); list.Add(c); if(k<0)break; i=k;} return list.ToArray(); }
static bool Contains(byte[] h,string s){return IndexOf(h,Encoding.ASCII.GetBytes(s))>=0;}
static int IndexOf(byte[] h,byte[] n,int s=0){for(int i=s;i<=h.Length-n.Length;i++){bool ok=true;for(int j=0;j<n.Length;j++)if(h[i+j]!=n[j]){ok=false;break;}if(ok)return i;}return -1;}
}
