import Foundation
import ImageIO
import UIKit

/// The store art the App bundles (design/components/Launcher, Cover art): the launcher's cover is
/// the library hero (wide) or the hero capsule (the portrait promotional art) by its shape, and
/// onboarding shows the library capsule. `design/build.mjs` copies the files here from
/// `design/assets/Covers/`.
enum Artwork: String {
    case capsule = "library_capsule_2x"
    case hero = "library_hero"
    case promo = "hero_capsule_2x"
}

@MainActor
final class ArtworkStore {
    private let memory = NSCache<NSString, UIImage>()
    /// The cover's art decodes at full size (the library hero is 1920 wide, the hero capsule 896
    /// tall): the cover crops a slice of it. Released before the game starts (`releaseMemory`).
    static let coverPixels: CGFloat = 1920

    init() {
        memory.totalCostLimit = 96 << 20
    }

    /// Decodes a downsampled bitmap so the art never costs more than the view needs: the game
    /// later shares this process.
    func image(_ artwork: Artwork, maxPixel: CGFloat) async -> UIImage? {
        let key = "\(artwork.rawValue)#\(Int(maxPixel))" as NSString
        if let hit = memory.object(forKey: key) { return hit }
        guard let file = Bundle.main.url(forResource: artwork.rawValue, withExtension: "jpg") else { return nil }
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
}
