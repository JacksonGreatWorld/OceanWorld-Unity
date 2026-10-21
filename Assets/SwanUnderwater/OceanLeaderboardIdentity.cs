using System;
using System.IO;
using System.Security.Cryptography;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
#endif

namespace SwanUnderwater
{
    /// <summary>Call Open and Sign on a worker thread. Device keys are held by the platform keystore; only public keys and signatures cross the bridge.</summary>
    public sealed class OceanLeaderboardIdentity : IDisposable
    {
        const string Alias="ocean.leaderboard.rsa.v1";
        public string DeviceId { get; private set; }
        public string PublicKeySpkiB64 { get; private set; }
        public bool IsTestIdentity { get; private set; }
#if UNITY_ANDROID && !UNITY_EDITOR
        public static OceanLeaderboardIdentity Open(string unusedDirectory)
        {
            AndroidJNI.AttachCurrentThread();
            try
            {
                using(var store=OpenStore())
                {
                    if(!store.Call<bool>("containsAlias",Alias))
                    {
                        using(var generatorClass=new AndroidJavaClass("java.security.KeyPairGenerator"))
                        using(var generator=generatorClass.CallStatic<AndroidJavaObject>("getInstance","RSA","AndroidKeyStore"))
                        using(var builder=new AndroidJavaObject("android.security.keystore.KeyGenParameterSpec$Builder",Alias,12)) // PURPOSE_SIGN | PURPOSE_VERIFY
                        {
                            using(builder.Call<AndroidJavaObject>("setKeySize",2048)) {}
                            using(builder.Call<AndroidJavaObject>("setDigests",(object)new string[]{"SHA-256"})) {}
                            using(builder.Call<AndroidJavaObject>("setSignaturePaddings",(object)new string[]{"PKCS1"})) {}
                            using(builder.Call<AndroidJavaObject>("setUserAuthenticationRequired",false)) {}
                            using(var spec=builder.Call<AndroidJavaObject>("build")) generator.Call("initialize",spec);
                            using(generator.Call<AndroidJavaObject>("generateKeyPair")) {}
                        }
                    }
                    using(var certificate=store.Call<AndroidJavaObject>("getCertificate",Alias))
                    using(var publicKey=certificate.Call<AndroidJavaObject>("getPublicKey"))
                    {
                        var identity=new OceanLeaderboardIdentity();identity.SetPublic(Bytes(publicKey.Call<sbyte[]>("getEncoded")));return identity;
                    }
                }
            }
            finally { AndroidJNI.DetachCurrentThread(); }
        }
        static AndroidJavaObject OpenStore()
        {
            using(var type=new AndroidJavaClass("java.security.KeyStore"))
            {
                var store=type.CallStatic<AndroidJavaObject>("getInstance","AndroidKeyStore");
                try
                {
                    // Explicit public overload: load(InputStream, char[]), both null. Avoid null-overload inference.
                    var method=AndroidJNI.GetMethodID(store.GetRawClass(),"load","(Ljava/io/InputStream;[C)V");
                    AndroidJNI.CallVoidMethod(store.GetRawObject(),method,new jvalue[2]);
                    var exception=AndroidJNI.ExceptionOccurred();
                    if(exception!=IntPtr.Zero) { AndroidJNI.ExceptionClear();AndroidJNI.DeleteLocalRef(exception);throw new InvalidOperationException("AndroidKeyStore.load failed."); }
                    return store;
                }
                catch { store.Dispose();throw; }
            }
        }
        public byte[] Sign(byte[] payload)
        {
            AndroidJNI.AttachCurrentThread();
            try
            {
                using(var store=OpenStore())
                using(var key=store.Call<AndroidJavaObject>("getKey",Alias,null))
                using(var type=new AndroidJavaClass("java.security.Signature"))
                using(var signature=type.CallStatic<AndroidJavaObject>("getInstance","SHA256withRSA"))
                { signature.Call("initSign",key);signature.Call("update",(object)Signed(payload));return Bytes(signature.Call<sbyte[]>("sign")); }
            }
            finally { AndroidJNI.DetachCurrentThread(); }
        }
        static byte[] Bytes(sbyte[] source) { var result=new byte[source.Length];Buffer.BlockCopy(source,0,result,0,result.Length);return result; }
        static sbyte[] Signed(byte[] source) { var result=new sbyte[source.Length];Buffer.BlockCopy(source,0,result,0,result.Length);return result; }
        public void Dispose() {}

#else
        RSA rsa;
        public static OceanLeaderboardIdentity Open(string directory)
        {
#if !UNITY_EDITOR && !OCEAN_LEADERBOARD_TESTS
            throw new PlatformNotSupportedException("Leaderboard signing requires AndroidKeyStore. Exportable fallback is tests/Editor only.");
#else
            Directory.CreateDirectory(directory);string path=Path.Combine(directory,"editor-test-rsa.xml");
            var identity=new OceanLeaderboardIdentity { rsa=new RSACryptoServiceProvider(2048) { PersistKeyInCsp=false },IsTestIdentity=true };
            try
            {
                if(File.Exists(path))identity.rsa.FromXmlString(File.ReadAllText(path));
                else
                {
                    string temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
                    try { File.WriteAllText(temporary,identity.rsa.ToXmlString(true));File.Move(temporary,path); }
                    finally { if(File.Exists(temporary))File.Delete(temporary); }
                }
                if(identity.rsa.KeySize!=2048)throw new CryptographicException("Expected 2048-bit test key.");
                identity.SetPublic(SubjectPublicKeyInfo(identity.rsa.ExportParameters(false)));return identity;
            }
            catch { identity.Dispose();throw; }
#endif
        }
        public byte[] Sign(byte[] payload) => rsa.SignData(payload,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        public void Dispose() { rsa?.Dispose();rsa=null; }
#endif
        void SetPublic(byte[] spki)
        {
            PublicKeySpkiB64=Convert.ToBase64String(spki);
            using(var sha=SHA256.Create())DeviceId=BitConverter.ToString(sha.ComputeHash(spki)).Replace("-","").ToLowerInvariant();
        }
        // RFC 5280 RSA SubjectPublicKeyInfo. Used only by the nonproduction test key path.
        public static byte[] SubjectPublicKeyInfo(RSAParameters key)
        {
            byte[] algorithm={0x30,0x0d,0x06,0x09,0x2a,0x86,0x48,0x86,0xf7,0x0d,0x01,0x01,0x01,0x05,0x00};
            byte[] rsaKey=Der(0x30,Join(Integer(key.Modulus),Integer(key.Exponent)));
            return Der(0x30,Join(algorithm,Der(0x03,Join(new byte[]{0},rsaKey))));
        }
        static byte[] Integer(byte[] value) { int start=0;while(start<value.Length-1&&value[start]==0)start++;var data=new byte[value.Length-start+((value[start]&128)!=0?1:0)];Buffer.BlockCopy(value,start,data,data.Length-(value.Length-start),value.Length-start);return Der(2,data); }
        static byte[] Join(byte[] a,byte[] b) { var result=new byte[a.Length+b.Length];Buffer.BlockCopy(a,0,result,0,a.Length);Buffer.BlockCopy(b,0,result,a.Length,b.Length);return result; }
        static byte[] Der(byte tag,byte[] value)
        {
            byte[] length=value.Length<128?new[]{(byte)value.Length}:value.Length<256?new[]{(byte)0x81,(byte)value.Length}:new[]{(byte)0x82,(byte)(value.Length>>8),(byte)value.Length};
            return Join(Join(new[]{tag},length),value);
        }
    }
}
