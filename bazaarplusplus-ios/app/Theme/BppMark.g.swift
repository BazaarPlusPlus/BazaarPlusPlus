// The BazaarPlusPlus brand mark, generated from design/assets/Logos/bpp-mark.svg
// by `just design::build`. Do not edit.
import SwiftUI

/// The brand mark: a chevron over a gem, drawn at any size from its flat polygons.
struct BppMark: View {
    struct Polygon {
        let points: [CGPoint]
        let fill: Color
    }

    static let grid: CGFloat = 32
    static let polygons = [
        Polygon(points: [CGPoint(x: 16, y: 3), CGPoint(x: 31, y: 18), CGPoint(x: 23.5, y: 18), CGPoint(x: 16, y: 10.5), CGPoint(x: 8.5, y: 18), CGPoint(x: 1, y: 18)], fill: Color(.sRGB, red: 237.0 / 255, green: 235.0 / 255, blue: 231.0 / 255, opacity: 1)),
        Polygon(points: [CGPoint(x: 16, y: 13), CGPoint(x: 23.75, y: 20.75), CGPoint(x: 16, y: 28.5), CGPoint(x: 8.25, y: 20.75)], fill: Color(.sRGB, red: 238.0 / 255, green: 142.0 / 255, blue: 31.0 / 255, opacity: 1)),
    ]

    var size: CGFloat

    var body: some View {
        let scale = size / Self.grid
        ZStack {
            ForEach(Self.polygons.indices, id: \.self) { index in
                let polygon = Self.polygons[index]
                Path { path in
                    path.addLines(polygon.points.map { CGPoint(x: $0.x * scale, y: $0.y * scale) })
                    path.closeSubpath()
                }
                .fill(polygon.fill)
            }
        }
        .frame(width: size, height: size)
        .accessibilityHidden(true)
    }
}
