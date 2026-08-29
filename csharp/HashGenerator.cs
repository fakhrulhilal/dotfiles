#!/usr/bin/env dotnet

using System.Security.Cryptography;
using System.Text;

if (args is not { Length: >= 3 }) {
    Console.Error.WriteLine("Usage: HashGenerator <algorithm> <secret> <message> <encoding>");
    Environment.Exit(1);
}

var (algorithmName, secret, message) = (args[0], args[1], args[2]);
if (string.IsNullOrEmpty(secret)) {
    Console.Error.WriteLine("Secret cannot be empty");
    Environment.Exit(1);
}

if (string.IsNullOrEmpty(message)) {
    Console.Error.WriteLine("Message cannot be empty");
    Environment.Exit(1);
}

var encoding = args.Length > 3 ? args[3] : "hex";
var key = Encoding.UTF8.GetBytes(secret);
using HashAlgorithm algorithm = algorithmName.ToLowerInvariant() switch {
    var kind when string.IsNullOrEmpty(kind) => throw new ArgumentException("Algorithm name cannot be empty"),
    "hmacsha1" or "sha1" => new HMACSHA1(key),
    "hmacsha256" or "sha256" => new HMACSHA256(key),
    "hmacsha384" or "sha384" => new HMACSHA384(key),
    "hmacsha512" or "sha512" => new HMACSHA512(key),
    "hmac3sha256" => new HMACSHA3_256(key),
    "hmac3sha384" => new HMACSHA3_384(key),
    "hmac3sha512" => new HMACSHA3_512(key),
    var kind => throw new ArgumentException($"Unsupported algorithm: {kind}")
};
var hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(message));
var encoded = encoding.ToLowerInvariant() switch {
    "hex" => Convert.ToHexStringLower(hash),
    "base64" => Convert.ToBase64String(hash),
    var kind => throw new ArgumentException($"Unsupported encoding: {kind}")
};
Console.WriteLine(encoded);
