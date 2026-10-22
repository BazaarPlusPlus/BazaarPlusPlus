#nullable enable
using System.Text;
using System.Text.Json;
using BazaarPlusPlus.ManagedPayload;
using BazaarPlusPlus.TestSupport;
using Xunit;

namespace Updater.Tests;

public sealed class CryptoTests
{
    // BLAKE2b-512 of bytes i % 251, from Python's hashlib.blake2b; "abc" is RFC 7693 Appendix A.
    [Theory]
    [InlineData(
        0,
        "786a02f742015903c6c6fd852552d272912f4740e15847618a86e217f71f5419d25e1031afee585313896444934eb04b903a685b1448b755d56f701afe9be2ce"
    )]
    [InlineData(
        127,
        "b6292669ccd38d5f01caae96ba272c76a879a45743afa0725d83b9ebb26665b731f1848c52f11972b6644f554c064fa90780dbbbf3a89d4fc31f67df3e5857ef"
    )]
    [InlineData(
        128,
        "2319e3789c47e2daa5fe807f61bec2a1a6537fa03f19ff32e87eecbfd64b7e0e8ccff439ac333b040f19b0c4ddd11a61e24ac1fe0f10a039806c5dcc0da3d115"
    )]
    [InlineData(
        129,
        "f59711d44a031d5f97a9413c065d1e614c417ede998590325f49bad2fd444d3e4418be19aec4e11449ac1a57207898bc57d76a1bcf3566292c20c683a5c4648f"
    )]
    [InlineData(
        256,
        "93463ac058b6163eb43be3f5bb32b28541498f4e3366f1effe253ad44e1e076e41c3616046027c82a7124f8f4746668ad10b12e8e25a95ac8f3151df01cd5a93"
    )]
    [InlineData(
        1000,
        "c11e1c0340bd7e5a1b275f1230c962fad215ecb1391486e74e31b960a2f2996381a5fad092da06841d5f26e38f6ecfeaf441acbcd1c2de61aef121e7927175f5"
    )]
    public void Blake2b_512_matches_reference_digests(int length, string expected)
    {
        var data = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        Assert.Equal(expected, Hex(Blake2b.Hash512(data)));
    }

    [Fact]
    public void Blake2b_512_matches_rfc_7693_abc()
    {
        Assert.Equal(
            "ba80a53f981c4d0d6a2797b69f12f6e94c212f14685ac4b74b12bb6fdbffa2d17d87c5392aab792dc252d5de4533cc9518d38aa8dbf1925ab92386edd4009923",
            Hex(Blake2b.Hash512(Encoding.ASCII.GetBytes("abc")))
        );
    }

    // RFC 8032 §7.1 tests 1-3.
    [Theory]
    [InlineData(
        "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60",
        "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a",
        "",
        "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b"
    )]
    [InlineData(
        "4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb",
        "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c",
        "72",
        "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00"
    )]
    [InlineData(
        "c5aa8df43f9f837bedb7442f31dcb7b166d38535076f094b85ce3a2e0b4458f7",
        "fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025",
        "af82",
        "6291d657deec24024827e69c3abe01a30ce548a284743a445e3680d7db5ac3ac18ff9b538d16f290ae67f760984dc6594a7c15e9716ed28dc027beceea1ec40a"
    )]
    public void Ed25519_verifies_rfc_8032_vectors_and_the_test_signer_reproduces_them(
        string seed,
        string publicKey,
        string message,
        string signature
    )
    {
        var signer = new TestSigner(Bytes(seed), new byte[8]);
        Assert.Equal(publicKey, Hex(signer.PublicKey));
        Assert.Equal(signature, Hex(signer.Sign(Bytes(message))));

        Assert.True(Ed25519.Verify(Bytes(publicKey), Bytes(message), Bytes(signature)));
        var tampered = Bytes(signature);
        tampered[5] ^= 1;
        Assert.False(Ed25519.Verify(Bytes(publicKey), Bytes(message), tampered));
        Assert.False(Ed25519.Verify(Bytes(publicKey), Bytes(message + "00"), Bytes(signature)));
    }

    [Fact]
    public void Ed25519_rejects_a_non_canonical_s()
    {
        var publicKey = Bytes("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        var signature = Bytes(
            "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b"
        );
        // S + L: the same point equation, but not the encoding RFC 8032 accepts.
        var l = Bytes("edd3f55c1a631258d69cf7a2def9de1400000000000000000000000000000010");
        var carry = 0;
        for (var i = 0; i < 32; i++)
        {
            var sum = signature[32 + i] + l[i] + carry;
            signature[32 + i] = (byte)sum;
            carry = sum >> 8;
        }
        Assert.False(Ed25519.Verify(publicKey, Array.Empty<byte>(), signature));
    }

    // A file signed with the release key through release/android-release.mjs signWithUpdaterKey
    // (the Tauri signer): the whole path, prehash included, under the key the Updater embeds.
    [Fact]
    public void Release_key_verifies_a_tauri_signer_signature()
    {
        const string signature =
            "dW50cnVzdGVkIGNvbW1lbnQ6IHNpZ25hdHVyZSBmcm9tIHRhdXJpIHNlY3JldCBrZXkKUlVUdnp0cnFoU1BnOVF2Kys5cWZSSTIxUEFCMzFNOFl5bkdMSkJFY2xwV1dYTUZKWWlEKzNwU3hmUlhYa3hPUTdaYmZTNkxtRWoyUmxaUCtkaVpwZGpWNVVuaitza1luR0E0PQp0cnVzdGVkIGNvbW1lbnQ6IHRpbWVzdGFtcDoxNzkyNjg1MDcwCWZpbGU6bXNnLnR4dAozdWlvbGJhK0xIQWg0ZkN5Ukl2VHBTdkJCaHc4Q0V4NGFtbTRTcS9FM0p3R1pnNk5jTCtnaGttSXNhR29vYUIwbkRGbVRTenBPSHIxanJ4UHhlTURBdz09Cg==";
        var data = Encoding.ASCII.GetBytes("hello bpp\n");

        Assert.True(
            Minisign.Verify(ReleaseSigningKey.PublicKey, data, signature, out var failure),
            failure
        );
        Assert.False(
            Minisign.Verify(
                ReleaseSigningKey.PublicKey,
                Encoding.ASCII.GetBytes("hello bpp!"),
                signature,
                out failure
            )
        );
        Assert.Equal("signature_invalid", failure);
        Assert.False(Minisign.Verify(TestSigner.Default.MinisignKey, data, signature, out failure));
        Assert.Equal("signature_key_id_mismatch", failure);
    }

    // The Tauri signer writes only prehashed "ED" signatures; minisign's raw "Ed" form is refused
    // even when it would verify.
    [Fact]
    public void A_signature_that_is_not_prehashed_is_refused()
    {
        var data = Encoding.ASCII.GetBytes("hello bpp\n");
        var signer = TestSigner.Default;
        var raw = signer.Sign(data);
        var box =
            "untrusted comment: raw signature\n"
            + Convert.ToBase64String(
                new byte[] { (byte)'E', (byte)'d' }
                    .Concat(signer.KeyId)
                    .Concat(raw)
                    .ToArray()
            )
            + "\ntrusted comment: timestamp:0\n"
            + Convert.ToBase64String(
                signer.Sign(raw.Concat(Encoding.UTF8.GetBytes("timestamp:0")).ToArray())
            )
            + "\n";

        Assert.False(Minisign.Verify(signer.MinisignKey, data, box, out var failure));
        Assert.Equal("signature_algorithm_unsupported", failure);
    }

    // The published 5.7.0 Windows updater signature (bppinstaller.bazaarplusplus.com/latest.json)
    // over BazaarPlusPlus_5.7.0_x64-setup.exe, whose BLAKE2b-512 Python's hashlib computed from the
    // published file (8872188 bytes, sha256 91c2386a…f25c).
    [Fact]
    public void Release_key_verifies_the_published_5_7_0_updater_signature()
    {
        const string signature =
            "dW50cnVzdGVkIGNvbW1lbnQ6IHNpZ25hdHVyZSBmcm9tIHRhdXJpIHNlY3JldCBrZXkKUlVUdnp0cnFoU1BnOVRtb08vczlzN3BUYXR4WVpYc0NvN1FpcmFpTnJhNVVhNm9uZjdRN0ZCaWFQMlFlekJkanY3ZTdkaXNvL201ZmhIbW5hN3B4YWJic2g3M3p5NnN2c3dzPQp0cnVzdGVkIGNvbW1lbnQ6IHRpbWVzdGFtcDoxNzkxNDM0NjM4CWZpbGU6QmF6YWFyUGx1c1BsdXNfNS43LjBfeDY0LXNldHVwLmV4ZQpQd3ZybzdrQU9Za0cvMUc0ZDFWaEFIbUFscGlDb2pSMFN1UHRZUEpjVE5GK0JkWVYrOHB5L0gwa3RTNUpyUEFaTmVSbXBpQWNzNFQwZWNvZlE4YVJBQT09Cg==";
        var digest = Bytes(
            "b4cdff95ed2f728233662dbec79679a0c6bdffe36290386a7a019972decf7cf6404e9f31d9abb7a07f03d3e9b37f46c4b752cb64572f07bd431728cafd06b430"
        );

        Assert.True(
            Minisign.VerifyPrehashed(
                ReleaseSigningKey.PublicKey,
                digest,
                signature,
                out var failure
            ),
            failure
        );
        digest[0] ^= 1;
        Assert.False(
            Minisign.VerifyPrehashed(ReleaseSigningKey.PublicKey, digest, signature, out _)
        );
    }

    [Fact]
    public void A_tampered_trusted_comment_fails_the_global_signature()
    {
        var data = Encoding.ASCII.GetBytes("payload");
        var box = Encoding.UTF8.GetString(
            Convert.FromBase64String(TestSigner.Default.SignMinisign(data))
        );
        var tampered = box.Replace("file:test", "file:evil", StringComparison.Ordinal);

        Assert.True(Minisign.Verify(TestSigner.Default.MinisignKey, data, box, out _));
        Assert.False(
            Minisign.Verify(TestSigner.Default.MinisignKey, data, tampered, out var failure)
        );
        Assert.Equal("trusted_comment_signature_invalid", failure);
    }

    [Fact]
    public void The_embedded_key_is_the_tauri_updater_public_key()
    {
        var config = JsonDocument.Parse(
            TestInputs.Scratch(
                Path.Combine(AppContext.BaseDirectory, "fixtures", "tauri.conf.json")
            )
        );
        var pubkey = config
            .RootElement.GetProperty("plugins")
            .GetProperty("updater")
            .GetProperty("pubkey")
            .GetString()!;
        var lines = Encoding.UTF8.GetString(Convert.FromBase64String(pubkey)).Split('\n');

        Assert.Equal(ReleaseSigningKey.PublicKeyLine, lines[1]);
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(bytes);

    private static byte[] Bytes(string hex) => Convert.FromHexString(hex);
}
