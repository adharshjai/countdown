// Draws windows/Countdown.ico: a blue tile with a three-quarter countdown ring.
// macOS only (CoreGraphics). Run from the repository root:
//     swift windows/tools/make-icon.swift windows/Countdown.ico
import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

func render(_ size: Int) -> CGImage {
    let s = CGFloat(size)
    let space = CGColorSpace(name: CGColorSpace.sRGB)!
    let ctx = CGContext(data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: size * 4,
                        space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    let inset = max(0.5, s * 0.04)
    let tile = CGRect(x: inset, y: inset, width: s - 2 * inset, height: s - 2 * inset)
    let corner = tile.width * 0.24

    ctx.saveGState()
    ctx.addPath(CGPath(roundedRect: tile, cornerWidth: corner, cornerHeight: corner, transform: nil))
    ctx.clip()
    let top = CGColor(srgbRed: 0.33, green: 0.55, blue: 1.00, alpha: 1)
    let bottom = CGColor(srgbRed: 0.20, green: 0.33, blue: 0.93, alpha: 1)
    let gradient = CGGradient(colorsSpace: space, colors: [top, bottom] as CFArray, locations: [0, 1])!
    ctx.drawLinearGradient(gradient, start: CGPoint(x: 0, y: s), end: CGPoint(x: 0, y: 0), options: [])
    ctx.restoreGState()

    let center = CGPoint(x: s / 2, y: s / 2)
    let radius = tile.width * 0.29
    ctx.setLineWidth(max(1.6, tile.width * 0.12))
    ctx.setLineCap(.round)
    ctx.setStrokeColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 0.32))
    ctx.addArc(center: center, radius: radius, startAngle: 0, endAngle: .pi * 2, clockwise: false)
    ctx.strokePath()
    // From twelve o'clock, clockwise through three quarters: the time already counted down.
    ctx.setStrokeColor(CGColor(srgbRed: 1, green: 1, blue: 1, alpha: 1))
    ctx.addArc(center: center, radius: radius, startAngle: .pi / 2, endAngle: .pi / 2 - 1.5 * .pi, clockwise: true)
    ctx.strokePath()
    return ctx.makeImage()!
}

extension Data {
    mutating func le16(_ v: Int) { var x = UInt16(v).littleEndian; Swift.withUnsafeBytes(of: &x) { append(contentsOf: $0) } }
    mutating func le32(_ v: Int) { var x = UInt32(v).littleEndian; Swift.withUnsafeBytes(of: &x) { append(contentsOf: $0) } }
}

/// A 32-bit BMP icon entry: header, straight-alpha BGRA rows bottom-up, then the AND mask.
func bmpEntry(_ image: CGImage) -> Data {
    let w = image.width, h = image.height
    let space = CGColorSpace(name: CGColorSpace.sRGB)!
    var rgba = [UInt8](repeating: 0, count: w * h * 4)
    let ctx = CGContext(data: &rgba, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4,
                        space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
    let maskRow = ((w + 31) / 32) * 4
    var out = Data()
    out.le32(40); out.le32(w); out.le32(h * 2); out.le16(1); out.le16(32); out.le32(0)
    out.le32(w * h * 4 + maskRow * h); out.le32(0); out.le32(0); out.le32(0); out.le32(0)
    for y in (0..<h).reversed() {           // BMP rows run bottom-up
        for x in 0..<w {
            let i = (y * w + x) * 4
            let a = Int(rgba[i + 3])
            func straight(_ c: UInt8) -> UInt8 { a == 0 ? 0 : UInt8(min(255, (Int(c) * 255 + a / 2) / a)) }
            out.append(straight(rgba[i + 2])); out.append(straight(rgba[i + 1])); out.append(straight(rgba[i])); out.append(UInt8(a))
        }
    }
    for y in (0..<h).reversed() {
        var row = [UInt8](repeating: 0, count: maskRow)
        for x in 0..<w where rgba[(y * w + x) * 4 + 3] == 0 { row[x / 8] |= UInt8(0x80 >> (x % 8)) }
        out.append(contentsOf: row)
    }
    return out
}

func pngEntry(_ image: CGImage) -> Data {
    let data = NSMutableData()
    let dest = CGImageDestinationCreateWithData(data, UTType.png.identifier as CFString, 1, nil)!
    CGImageDestinationAddImage(dest, image, nil)
    CGImageDestinationFinalize(dest)
    return data as Data
}

let output = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "Countdown.ico"
var entries: [(size: Int, data: Data)] = [16, 20, 24, 32, 40, 48, 64].map { ($0, bmpEntry(render($0))) }
entries.append((256, pngEntry(render(256))))

var ico = Data()
ico.le16(0); ico.le16(1); ico.le16(entries.count)
var offset = 6 + 16 * entries.count
for entry in entries {
    let side = entry.size >= 256 ? 0 : entry.size
    ico.append(UInt8(side)); ico.append(UInt8(side)); ico.append(0); ico.append(0)
    ico.le16(1); ico.le16(32); ico.le32(entry.data.count); ico.le32(offset)
    offset += entry.data.count
}
for entry in entries { ico.append(entry.data) }
try! ico.write(to: URL(fileURLWithPath: output))
print("wrote \(output): \(entries.count) sizes, \(ico.count) bytes")
