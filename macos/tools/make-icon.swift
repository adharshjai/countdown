// Draws the macOS app icon for Countdown: a deep blue tile with a three-quarter countdown ring that
// starts at twelve o'clock and runs clockwise, the same mark as windows/Countdown.ico.
//
//     swift macos/tools/make-icon.swift macos/Resources [--preview]
//
// Writes OUTDIR/AppIcon.icns from ten sizes, each drawn at its own pixel size so 16 and 32 px stay
// crisp. With --preview it also writes icon-1024.png and preview.png (every size on light and dark).
// Uses CoreGraphics, QuartzCore, ImageIO and Foundation only; every shape is drawn here.
//
// Geometry follows Apple's macOS app icon template: on a 1024 canvas the body is an 824 x 824 rounded square
// with continuous corners of radius 185.4 (rendered by a CALayer whose cornerCurve is .continuous), centred in
// a 100 px transparent margin that holds a soft drop shadow (12 px down, 28 px blur; black at 19%, like
// macOS's own icons, and 30% at 16 and 32 px, where Apple's icons carry a firmer one-pixel halo).
import CoreGraphics
import Foundation
import ImageIO
import QuartzCore

let sRGB = CGColorSpace(name: CGColorSpace.sRGB)!

func color(_ hex: UInt32, _ alpha: CGFloat = 1) -> CGColor {
    CGColor(colorSpace: sRGB, components: [CGFloat((hex >> 16) & 0xFF) / 255, CGFloat((hex >> 8) & 0xFF) / 255,
                                            CGFloat(hex & 0xFF) / 255, alpha])!
}

func gradient(_ stops: [(CGFloat, CGColor)]) -> CGGradient {
    CGGradient(colorsSpace: sRGB, colors: stops.map { $0.1 } as CFArray, locations: stops.map { $0.0 })!
}

func canvas(_ width: Int, _ height: Int) -> CGContext {
    let ctx = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                        space: sRGB, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.setAllowsAntialiasing(true)
    ctx.setShouldAntialias(true)
    ctx.interpolationQuality = .high
    return ctx
}

func circle(_ center: CGPoint, _ radius: CGFloat) -> CGRect {
    CGRect(x: center.x - radius, y: center.y - radius, width: radius * 2, height: radius * 2)
}

/// Apple's continuous-corner rounded square, rendered by Core Animation into a DeviceGray mask
/// (white inside). With `border` > 0 only an inner band of that width along the outline is white.
func squircleMask(size: Int, body: CGRect, radius: CGFloat, border: CGFloat = 0) -> CGImage {
    let ctx = CGContext(data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: 0,
                        space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue)!
    ctx.setFillColor(gray: 0, alpha: 1)
    ctx.fill(CGRect(x: 0, y: 0, width: size, height: size))
    let layer = CALayer()
    layer.bounds = CGRect(origin: .zero, size: body.size)
    layer.cornerRadius = radius
    layer.cornerCurve = .continuous
    if border > 0 {
        layer.borderWidth = border
        layer.borderColor = CGColor(gray: 1, alpha: 1)
    } else {
        layer.backgroundColor = CGColor(gray: 1, alpha: 1)
    }
    ctx.translateBy(x: body.minX, y: body.minY)
    layer.render(in: ctx)
    return ctx.makeImage()!
}

/// Shades the inside of `shape` with the shadow of everything outside it (an inner shadow or, in white, an inner light).
func innerShadow(_ ctx: CGContext, _ shape: CGPath, offset: CGSize, blur: CGFloat, color: CGColor) {
    let reach = blur * 3 + max(abs(offset.width), abs(offset.height)) + 4
    let far = CGFloat(ctx.width) * 4        // caster drawn off-canvas; only its shadow comes back
    let negative = CGMutablePath()
    negative.addRect(shape.boundingBoxOfPath.insetBy(dx: -reach, dy: -reach))
    negative.addPath(shape)
    ctx.saveGState()
    ctx.addPath(shape)
    ctx.clip()
    ctx.setShadow(offset: CGSize(width: offset.width + far, height: offset.height), blur: blur, color: color)
    ctx.translateBy(x: -far, y: 0)
    ctx.addPath(negative)
    ctx.setFillColor(gray: 0, alpha: 1)
    ctx.fillPath(using: .evenOdd)
    ctx.restoreGState()
}

/// Paints a band of `width` just inside the outline of `shape`.
func innerRim(_ ctx: CGContext, _ shape: CGPath, width: CGFloat, color: CGColor) {
    ctx.saveGState()
    ctx.addPath(shape)
    ctx.clip()
    ctx.addPath(shape)
    ctx.setLineWidth(width * 2)
    ctx.setStrokeColor(color)
    ctx.strokePath()
    ctx.restoreGState()
}

/// Everything that depends on the pixel size. Small sizes snap the body to whole pixels (Apple's own 16 px
/// icons use a 12 px body), get a bigger, thicker, pixel-aligned ring and drop the finer effects.
struct Look {
    let px: Int
    let k: CGFloat            // px / 1024, scales the effects
    let body: CGRect
    let corner: CGFloat
    let ringRadius: CGFloat   // centre line of the ring, in px
    let ringWidth: CGFloat    // stroke width, in px
    let trackAlpha: CGFloat
    let level: Int            // detail: 0 at 16 px, 1 at 32, 2 at 64, 3 at 128, 4 at 256 and up

    init(_ px: Int) {
        self.px = px
        k = CGFloat(px) / 1024
        let side = 2 * (412 * k).rounded()          // 824 at 1024; whole, even pixels at the small sizes
        let inset = (CGFloat(px) - side) / 2
        body = CGRect(x: inset, y: inset, width: side, height: side)
        corner = side * 185.4 / 824
        switch px {
        case ...16: (ringRadius, ringWidth, trackAlpha, level) = (4, 2, 0.32, 0)
        case ...32: (ringRadius, ringWidth, trackAlpha, level) = (7, 4, 0.32, 1)
        case ...64: (ringRadius, ringWidth, trackAlpha, level) = (14.5, 7, 0.28, 2)
        case ...128: (ringRadius, ringWidth, trackAlpha, level) = (29.5, 13, 0.28, 3)
        default: (ringRadius, ringWidth, trackAlpha, level) = (236 * k, 104 * k, 0.28, 4)
        }
    }
}

/// The opaque body: background, ring and rim light, clipped to the squircle. Transparent outside it.
func renderBody(_ look: Look) -> CGImage {
    let s = CGFloat(look.px), k = look.k, body = look.body, level = look.level
    let full = CGRect(x: 0, y: 0, width: s, height: s)
    let ctx = canvas(look.px, look.px)
    ctx.clip(to: full, mask: squircleMask(size: look.px, body: body, radius: look.corner))

    // Background: a rich vertical blue, lighter at the top, with a soft light falling from above.
    let base = gradient([(0, color(0x4D9BFF)), (0.5, color(0x3370F4)), (1, color(0x2A50E2))])
    ctx.drawLinearGradient(base, start: CGPoint(x: 0, y: body.maxY), end: CGPoint(x: 0, y: body.minY),
                           options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
    if level >= 1 {
        ctx.saveGState()
        ctx.translateBy(x: body.midX, y: body.maxY)
        ctx.scaleBy(x: 1, y: 0.7)
        let sheen = gradient([(0, color(0xFFFFFF, 0.14)), (1, color(0xFFFFFF, 0))])
        ctx.drawRadialGradient(sheen, startCenter: .zero, startRadius: 0, endCenter: .zero,
                               endRadius: body.width * 0.75, options: [])
        ctx.restoreGState()
    }

    // Ring geometry: the arc runs from twelve o'clock clockwise through three quarters, to nine o'clock.
    let c = CGPoint(x: s / 2, y: s / 2)
    let R = look.ringRadius, W = look.ringWidth
    let annulus = CGMutablePath()
    annulus.addEllipse(in: circle(c, R + W / 2))
    annulus.addEllipse(in: circle(c, R - W / 2))
    let arc = CGMutablePath()
    arc.addArc(center: c, radius: R, startAngle: .pi / 2, endAngle: -.pi, clockwise: true)
    // At 16 px flat ends keep the dim quarter a clean, readable gap.
    let arcShape = arc.copy(strokingWithWidth: W, lineCap: level == 0 ? .butt : .round, lineJoin: .round, miterLimit: 10)
    let end = CGPoint(x: c.x - R, y: c.y)
    let ringTop = c.y + R + W / 2, ringBottom = c.y - R - W / 2

    // Track: a frosted channel, softly shaded under its upper edges; from 256 px light catches its lower lip.
    ctx.saveGState()
    ctx.addPath(annulus)
    ctx.setFillColor(color(0xFFFFFF, look.trackAlpha))
    ctx.fillPath(using: .evenOdd)
    ctx.restoreGState()
    if level >= 2 {
        let outside = CGMutablePath()
        outside.addRect(full.insetBy(dx: -s, dy: -s))
        outside.addPath(annulus)
        func channelLight(offset: CGFloat, blur: CGFloat, _ shade: CGColor) {
            ctx.saveGState()
            ctx.addPath(annulus)
            ctx.clip(using: .evenOdd)
            ctx.setShadow(offset: CGSize(width: 4 * s, height: offset), blur: blur, color: shade)
            ctx.translateBy(x: -4 * s, y: 0)
            ctx.addPath(outside)
            ctx.setFillColor(color(0x000000))
            ctx.fillPath(using: .evenOdd)
            ctx.restoreGState()
        }
        channelLight(offset: -max(0.5, 8 * k), blur: max(1.5, 22 * k), color(0x0A1F78, level == 2 ? 0.14 : 0.2))
        if level >= 4 { channelLight(offset: 4 * k, blur: 6 * k, color(0xFFFFFF, 0.4)) }
    }

    // Contact shadow around the leading end, so the arc reads as lying on top of the track.
    if level >= 2 {
        ctx.saveGState()
        ctx.setShadow(offset: .zero, blur: max(1.5, 22 * k), color: color(0x0A1F78, level == 2 ? 0.4 : 0.5))
        ctx.setFillColor(color(0xFFFFFF))
        ctx.fillEllipse(in: circle(end, W / 2))
        ctx.restoreGState()
    }

    // Progress arc: white, gently shaded top to bottom, with a soft shadow below it.
    ctx.saveGState()
    if level >= 1 {
        ctx.setShadow(offset: CGSize(width: 0, height: -10 * k), blur: max(1, 24 * k),
                      color: color(0x0B1E80, level == 1 ? 0.3 : 0.4))
    }
    ctx.beginTransparencyLayer(auxiliaryInfo: nil)
    ctx.saveGState()
    ctx.addPath(arcShape)
    ctx.clip()
    let arcFill: CGGradient
    switch level {
    case 0: arcFill = gradient([(0, color(0xFFFFFF)), (1, color(0xFFFFFF))])
    case 1...3: arcFill = gradient([(0, color(0xFFFFFF)), (1, color(0xEEF4FF))])
    default: arcFill = gradient([(0, color(0xFAFCFF)), (1, color(0xE4EEFF))])
    }
    ctx.drawLinearGradient(arcFill, start: CGPoint(x: 0, y: ringTop), end: CGPoint(x: 0, y: ringBottom),
                           options: [.drawsBeforeStartLocation, .drawsAfterEndLocation])
    ctx.restoreGState()
    ctx.endTransparencyLayer()
    ctx.restoreGState()

    /// From 256 px the white parts get a glass edge: a bright rim inside the outline and a soft bevel.
    func glassEdge(_ shape: CGPath) {
        innerShadow(ctx, shape, offset: CGSize(width: 0, height: -5 * k), blur: 8 * k, color: color(0xFFFFFF, 0.9))
        innerShadow(ctx, shape, offset: CGSize(width: 0, height: 6 * k), blur: 14 * k, color: color(0x2A55C8, 0.18))
        innerRim(ctx, shape, width: 3 * k, color: color(0xFFFFFF, 0.85))
    }
    if level >= 4 { glassEdge(arcShape) }

    // Knob at the leading end: the moment the countdown has reached.
    if false && level >= 3 {
        let kr = W * 0.6
        let knob = CGPath(ellipseIn: circle(end, kr), transform: nil)
        ctx.saveGState()
        ctx.setShadow(offset: CGSize(width: 0, height: -max(0.5, 6 * k)), blur: max(1.5, 18 * k),
                      color: color(0x0A1F78, 0.45))
        ctx.beginTransparencyLayer(auxiliaryInfo: nil)
        ctx.saveGState()
        ctx.addPath(knob)
        ctx.clip()
        let knobFill = level >= 4 ? gradient([(0, color(0xFFFFFF)), (1, color(0xD5E5FF))])
                                  : gradient([(0, color(0xFFFFFF)), (1, color(0xDCE7FF))])
        ctx.drawLinearGradient(knobFill, start: CGPoint(x: 0, y: end.y + kr), end: CGPoint(x: 0, y: end.y - kr), options: [])
        ctx.restoreGState()
        ctx.endTransparencyLayer()
        ctx.restoreGState()
        if level >= 4 { glassEdge(knob) }
    }

    // Rim light: a thin glassy edge, bright at the top left, faint at the bottom right.
    if level >= 1 {
        let rim = squircleMask(size: look.px, body: body, radius: look.corner, border: max(1, 5 * k))
        let strength: CGFloat = level == 1 ? 0.35 : 0.6
        ctx.saveGState()
        ctx.clip(to: full, mask: rim)
        let light = gradient([(0, color(0xFFFFFF, strength)), (0.45, color(0xFFFFFF, 0.05)),
                              (0.75, color(0xFFFFFF, 0)), (1, color(0xFFFFFF, strength * 0.4))])
        ctx.drawLinearGradient(light, start: CGPoint(x: body.minX, y: body.maxY), end: CGPoint(x: body.maxX, y: body.minY),
                               options: [])
        ctx.restoreGState()
    }
    return ctx.makeImage()!
}

/// The full icon: the body on a transparent canvas, with its drop shadow in the margin.
func renderIcon(_ px: Int) -> CGImage {
    let look = Look(px)
    let s = CGFloat(px), k = look.k
    let ctx = canvas(px, px)
    ctx.setShadow(offset: CGSize(width: 0, height: -max(12 * k, 0.25)), blur: max(28 * k, 1.6),
                  color: color(0x000000, px <= 32 ? 0.30 : 0.19))
    ctx.draw(renderBody(look), in: CGRect(x: 0, y: 0, width: s, height: s))
    return ctx.makeImage()!
}

func writePNG(_ image: CGImage, to path: String) {
    guard let dest = CGImageDestinationCreateWithURL(URL(fileURLWithPath: path) as CFURL, "public.png" as CFString, 1, nil)
    else { fatalError("cannot create \(path)") }
    CGImageDestinationAddImage(dest, image, nil)
    guard CGImageDestinationFinalize(dest) else { fatalError("cannot write \(path)") }
}

/// Two rows, light (#F5F5F7) over dark (#1E1E1E), each with the icon at 512, 128, 64, 32 and 16 px, unscaled.
/// Rows are 480 px tall: the 512 px image's transparent margin overlaps the row edge, its body and shadow do not.
func renderPreview(_ icons: [Int: CGImage]) -> CGImage {
    let width = 1400, rowHeight = 480, height = rowHeight * 2
    let ctx = canvas(width, height)
    ctx.interpolationQuality = .none
    ctx.setFillColor(color(0xF5F5F7))
    ctx.fill(CGRect(x: 0, y: rowHeight, width: width, height: rowHeight))
    ctx.setFillColor(color(0x1E1E1E))
    ctx.fill(CGRect(x: 0, y: 0, width: width, height: rowHeight))
    let sizes = [512, 128, 64, 32, 16]
    let margin = 64
    let gap = (width - 2 * margin - sizes.reduce(0, +)) / (sizes.count - 1)
    for rowY in [rowHeight, 0] {
        var x = margin
        for size in sizes {
            let y = rowY + (rowHeight - size) / 2
            ctx.draw(icons[size]!, in: CGRect(x: x, y: y, width: size, height: size))
            x += size + gap
        }
    }
    return ctx.makeImage()!
}

let arguments = Array(CommandLine.arguments.dropFirst())
let outDir = arguments.first { !$0.hasPrefix("--") } ?? "."
let wantsPreview = arguments.contains("--preview")
let iconsetDir = (outDir as NSString).appendingPathComponent("AppIcon.iconset")
try? FileManager.default.removeItem(atPath: iconsetDir)
try! FileManager.default.createDirectory(atPath: iconsetDir, withIntermediateDirectories: true)

var icons: [Int: CGImage] = [:]
for px in [16, 32, 64, 128, 256, 512, 1024] { icons[px] = renderIcon(px) }

let iconset: [(String, Int)] = [
    ("icon_16x16.png", 16), ("icon_16x16@2x.png", 32), ("icon_32x32.png", 32), ("icon_32x32@2x.png", 64),
    ("icon_128x128.png", 128), ("icon_128x128@2x.png", 256), ("icon_256x256.png", 256), ("icon_256x256@2x.png", 512),
    ("icon_512x512.png", 512), ("icon_512x512@2x.png", 1024),
]
for (name, px) in iconset { writePNG(icons[px]!, to: (iconsetDir as NSString).appendingPathComponent(name)) }

// iconutil packs the ten files into one .icns, then the iconset folder goes.
let icns = (outDir as NSString).appendingPathComponent("AppIcon.icns")
let iconutil = Process()
iconutil.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
iconutil.arguments = ["-c", "icns", iconsetDir, "-o", icns]
try! iconutil.run()
iconutil.waitUntilExit()
guard iconutil.terminationStatus == 0 else { fatalError("iconutil failed") }
try! FileManager.default.removeItem(atPath: iconsetDir)
print("wrote \(icns)")

if wantsPreview {
    writePNG(icons[1024]!, to: (outDir as NSString).appendingPathComponent("icon-1024.png"))
    writePNG(renderPreview(icons), to: (outDir as NSString).appendingPathComponent("preview.png"))
    print("wrote icon-1024.png and preview.png")
}
