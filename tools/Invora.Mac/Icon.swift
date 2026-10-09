import CoreGraphics
import ImageIO
import Foundation
import UniformTypeIdentifiers

// Code-drawn brand monogram; no downloaded image or third-party font is needed.
let output = URL(fileURLWithPath: CommandLine.arguments[1])
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
for size in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let pixels = size * scale
        let context = CGContext(data: nil, width: pixels, height: pixels, bitsPerComponent: 8, bytesPerRow: pixels * 4,
                                space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.scaleBy(x: CGFloat(pixels) / 1024, y: CGFloat(pixels) / 1024)
        context.setFillColor(CGColor(red: 0.08, green: 0.25, blue: 0.20, alpha: 1))
        context.addPath(CGPath(roundedRect: CGRect(x: 70, y: 70, width: 884, height: 884), cornerWidth: 194, cornerHeight: 194, transform: nil)); context.fillPath()
        context.setFillColor(CGColor(red: 0.97, green: 0.97, blue: 0.93, alpha: 1))
        context.addPath(CGPath(roundedRect: CGRect(x: 360, y: 270, width: 116, height: 320), cornerWidth: 16, cornerHeight: 16, transform: nil)); context.fillPath()
        context.fillEllipse(in: CGRect(x: 353, y: 650, width: 130, height: 130))
        context.setFillColor(CGColor(red: 0.63, green: 0.74, blue: 0.44, alpha: 1))
        context.fillEllipse(in: CGRect(x: 574, y: 270, width: 124, height: 124))
        let name = "icon_\(size)x\(size)" + (scale == 2 ? "@2x" : "") + ".png"
        let target = CGImageDestinationCreateWithURL(output.appendingPathComponent(name) as CFURL, UTType.png.identifier as CFString, 1, nil)!
        CGImageDestinationAddImage(target, context.makeImage()!, nil)
        guard CGImageDestinationFinalize(target) else { fatalError("Icon could not be written") }
    }
}
