import Foundation
import ImageIO
import UIKit

/// Store artwork and blurb for one Steam app, resolved at runtime from the public
/// store API. Nothing here is bundled: Valve rotates hashed asset paths, and the
/// app must not redistribute store art inside the IPA.
struct StoreArtwork: Codable, Equatable {
    var name: String?
    var summary: String?
    var developers: [String] = []
    var publishers: [String] = []
    var capsule: URL?
    var hero: URL?
    var logo: URL?
    var header: URL?
    var screenshots: [URL] = []
    var fetchedAt: Date
}

@MainActor
final class ArtworkStore: ObservableObject {
    @Published private(set) var entries: [UInt32: StoreArtwork] = [:]
    private var inflight: Set<UInt32> = []
    private let memory = NSCache<NSString, UIImage>()
    private let root: URL
    private let session: URLSession
    private static let assetHost = "https://shared.akamai.steamstatic.com/store_item_assets/"
    private static let maxAge: TimeInterval = 3 * 24 * 3600

    init() {
        root = FileManager.default.urls(for: .cachesDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Artwork", isDirectory: true)
        let configuration = URLSessionConfiguration.default
        configuration.timeoutIntervalForRequest = 20
        configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
        configuration.urlCache = nil
        session = URLSession(configuration: configuration)
        memory.totalCostLimit = 96 << 20
    }

    func artwork(_ appId: UInt32?) -> StoreArtwork? { appId.flatMap { entries[$0] } }

    /// Serves the on-disk copy immediately and refreshes it when stale.
    func load(_ appId: UInt32) async {
        if entries[appId] == nil, let cached = readMetadata(appId) { entries[appId] = cached }
        if let current = entries[appId], Date().timeIntervalSince(current.fetchedAt) < Self.maxAge { return }
        guard inflight.insert(appId).inserted else { return }
        defer { inflight.remove(appId) }
        guard let fresh = await fetchMetadata(appId) else { return }
        entries[appId] = fresh
        writeMetadata(appId, fresh)
    }

    /// Downloads once into Caches, then decodes a downsampled bitmap so a 4K hero
    /// never costs more than the view needs — the game later shares this process.
    func image(_ url: URL, appId: UInt32, maxPixel: CGFloat) async -> UIImage? {
        let key = "\(url.absoluteString)#\(Int(maxPixel))" as NSString
        if let hit = memory.object(forKey: key) { return hit }
        let file = imageFile(url, appId: appId)
        if !FileManager.default.fileExists(atPath: file.path) {
            guard let (data, response) = try? await session.data(from: url),
                  (response as? HTTPURLResponse)?.statusCode == 200, !data.isEmpty else { return nil }
            try? FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
            try? data.write(to: file, options: .atomic)
        }
        let decoded = await Task.detached(priority: .userInitiated) { Self.downsample(file, maxPixel: maxPixel) }.value
        guard let decoded else { return nil }
        memory.setObject(decoded, forKey: key, cost: Int(decoded.size.width * decoded.size.height * decoded.scale * decoded.scale * 4))
        return decoded
    }

    func releaseMemory() { memory.removeAllObjects() }

    private nonisolated static func downsample(_ file: URL, maxPixel: CGFloat) -> UIImage? {
        guard let source = CGImageSourceCreateWithURL(file as CFURL, [kCGImageSourceShouldCache: false] as CFDictionary) else { return nil }
        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceShouldCacheImmediately: true,
            kCGImageSourceThumbnailMaxPixelSize: maxPixel,
        ]
        guard let image = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) else { return nil }
        return UIImage(cgImage: image)
    }

    private func directory(_ appId: UInt32) -> URL { root.appendingPathComponent(String(appId), isDirectory: true) }

    private func imageFile(_ url: URL, appId: UInt32) -> URL {
        // Hashed store paths are "<hash>/<name>"; keep both so a rotated asset gets a new file.
        let name = url.pathComponents.suffix(2).joined(separator: "_")
            .replacingOccurrences(of: "[^A-Za-z0-9._-]", with: "_", options: .regularExpression)
        return directory(appId).appendingPathComponent(name)
    }

    private func readMetadata(_ appId: UInt32) -> StoreArtwork? {
        guard let data = try? Data(contentsOf: directory(appId).appendingPathComponent("store.json")) else { return nil }
        return try? JSONDecoder().decode(StoreArtwork.self, from: data)
    }

    private func writeMetadata(_ appId: UInt32, _ value: StoreArtwork) {
        let folder = directory(appId)
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        if let data = try? JSONEncoder().encode(value) { try? data.write(to: folder.appendingPathComponent("store.json"), options: .atomic) }
        try? (folder as NSURL).setResourceValue(true, forKey: .isExcludedFromBackupKey)
    }

    private func fetchMetadata(_ appId: UInt32) async -> StoreArtwork? {
        let request: [String: Any] = [
            "ids": [["appid": appId]],
            "context": ["language": "schinese", "country_code": "US"],
            "data_request": ["include_assets": true, "include_screenshots": true, "include_basic_info": true],
        ]
        guard let body = try? JSONSerialization.data(withJSONObject: request),
              var components = URLComponents(string: "https://api.steampowered.com/IStoreBrowseService/GetItems/v1") else { return nil }
        components.queryItems = [URLQueryItem(name: "input_json", value: String(decoding: body, as: UTF8.self))]
        guard let url = components.url,
              let (data, response) = try? await session.data(from: url),
              (response as? HTTPURLResponse)?.statusCode == 200,
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let item = ((json["response"] as? [String: Any])?["store_items"] as? [[String: Any]])?.first,
              (item["success"] as? Int) == 1 else { return nil }
        let assets = item["assets"] as? [String: Any] ?? [:]
        let format = assets["asset_url_format"] as? String ?? "steam/apps/\(appId)/${FILENAME}"
        func asset(_ keys: String...) -> URL? {
            for key in keys { if let name = assets[key] as? String, !name.isEmpty {
                return URL(string: Self.assetHost + format.replacingOccurrences(of: "${FILENAME}", with: name))
            } }
            return nil
        }
        let basic = item["basic_info"] as? [String: Any] ?? [:]
        func names(_ key: String) -> [String] { (basic[key] as? [[String: Any]] ?? []).compactMap { $0["name"] as? String } }
        let shots = ((item["screenshots"] as? [String: Any])?["all_ages_screenshots"] as? [[String: Any]] ?? [])
            .sorted { ($0["ordinal"] as? Int ?? 0) < ($1["ordinal"] as? Int ?? 0) }
            .compactMap { ($0["filename"] as? String).flatMap { URL(string: Self.assetHost + $0) } }
        return StoreArtwork(
            name: item["name"] as? String,
            summary: basic["short_description"] as? String,
            developers: names("developers"), publishers: names("publishers"),
            capsule: asset("library_capsule_2x", "library_capsule"),
            hero: asset("library_hero", "library_hero_2x", "page_background"),
            // The store API omits the library logo; it lives at an unhashed path.
            logo: URL(string: Self.assetHost + "steam/apps/\(appId)/logo.png"),
            header: asset("header_2x", "header"),
            screenshots: shots,
            fetchedAt: Date())
    }
}
