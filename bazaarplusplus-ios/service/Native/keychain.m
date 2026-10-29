#import <Foundation/Foundation.h>
#import <Security/Security.h>
#define API __attribute__((visibility("default"), used))
static NSMutableDictionary *keychainQuery(void) {
    return [@{(__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: NSBundle.mainBundle.bundleIdentifier,
        (__bridge id)kSecAttrAccount: @"session"} mutableCopy];
}
API int bpp_steam_save_session(const char *json) {
    NSMutableDictionary *query = keychainQuery();
    if (!json || !*json) {
        OSStatus deleted = SecItemDelete((__bridge CFDictionaryRef)query);
        return deleted == errSecItemNotFound ? errSecSuccess : (int)deleted;
    }
    NSDictionary *attributes = @{
        (__bridge id)kSecValueData: [NSData dataWithBytes:json length:strlen(json)],
        (__bridge id)kSecAttrAccessible: (__bridge id)kSecAttrAccessibleWhenUnlockedThisDeviceOnly
    };
    OSStatus result = SecItemUpdate((__bridge CFDictionaryRef)query, (__bridge CFDictionaryRef)attributes);
    if (result == errSecItemNotFound) {
        [query addEntriesFromDictionary:attributes];
        result = SecItemAdd((__bridge CFDictionaryRef)query, NULL);
    }
    fprintf(stderr, "STEAM_KEYCHAIN_SAVE status=%d\n", (int)result);
    return (int)result;
}
API char *bpp_steam_load_session(void) {
    NSMutableDictionary *query = keychainQuery(); query[(__bridge id)kSecReturnData] = @YES;
    CFTypeRef value = NULL;
    if (SecItemCopyMatching((__bridge CFDictionaryRef)query, &value) != errSecSuccess) return NULL;
    NSData *data = CFBridgingRelease(value);
    char *copy = calloc(data.length + 1, 1); memcpy(copy, data.bytes, data.length); return copy;
}
