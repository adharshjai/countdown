// Draws the disk image window's background: the app on the left, Applications on the right,
// an arrow between them, and a one-line instruction. Writes background.png (640 x 400) and
// background@2x.png into the directory given. macOS only (CoreGraphics, CoreText).
//     swift macos/tools/make-dmg-background.swift macos/dmg
//
// It is light on purpose: Finder draws icon labels in dark text over a background picture
// even in Dark Mode, so a deep-coloured background would make the labels unreadable.
import CoreGraphics
import CoreText
import Foundation
import ImageIO

let outDir = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "."

let width: CGFloat = 640, height: CGFloat = 400
// Icon centres, in the window's top-left coordinates; macos/dmg/settings.py uses the same.
let appCentre = CGPoint(x: 170, y: 185)
let applicationsCentre = CGPoint(x: 470, y: 185)

func color(_ hex: UInt32, _ alpha: CGFloat = 1) -> CGColor {
    CGColor(srgbRed: CGFloat((hex >> 16) & 0xFF) / 255, green: CGFloat((hex >> 8) & 0xFF) / 255,
            blue: CGFloat(hex & 0xFF) / 255, alpha: alpha)
}

func render(scale: CGFloat) -> CGImage {
    let space = CGColorSpace(name: CGColorSpace.sRGB)!
    let ctx = CGContext(data: nil, width: Int(width * scale), height: Int(height * scale), bitsPerComponent: 8,
                        bytesPerRow: 0, space: space, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
    ctx.scaleBy(x: scale, y: scale)
    // Flip to top-left coordinates, like the Finder window.
    ctx.translateBy(x: 0, y: height)
    ctx.scaleBy(x: 1, y: -1)

    // Background: a soft vertical gradient with a faint glow behind the two icons.
    let top = color(0xF6F8FF)
    let bottom = color(0xE4EAF8)
    let gradient = CGGradient(colorsSpace: space, colors: [top, bottom] as CFArray, locations: [0, 1])!
    ctx.drawLinearGradient(gradient, start: .zero, end: CGPoint(x: 0, y: height), options: [])
    let glow = CGGradient(colorsSpace: space,
                          colors: [color(0xFFFFFF, 0.85), color(0xFFFFFF, 0)] as CFArray,
                          locations: [0, 1])!
    for centre in [appCentre, applicationsCentre] {
        ctx.drawRadialGradient(glow, startCenter: centre, startRadius: 0, endCenter: centre, endRadius: 120, options: [])
    }

    // The arrow: a gentle arc from the app towards Applications, with a trail of dots behind
    // it that reads as motion (Finder cannot animate a disk image window).
    let accent = color(0x3D64F0)
    let accentAlpha: CGFloat = 1
    let start = CGPoint(x: appCentre.x + 92, y: appCentre.y)
    let end = CGPoint(x: applicationsCentre.x - 96, y: applicationsCentre.y)
    let control = CGPoint(x: (start.x + end.x) / 2, y: appCentre.y - 34)
    func point(_ t: CGFloat) -> CGPoint {
        let u = 1 - t
        return CGPoint(x: u * u * start.x + 2 * u * t * control.x + t * t * end.x,
                       y: u * u * start.y + 2 * u * t * control.y + t * t * end.y)
    }
    ctx.setFillColor(accent)
    for (i, t) in [0.0, 0.1, 0.2].enumerated() {
        let p = point(CGFloat(t))
        let r = 2.2 + CGFloat(i) * 0.9
        ctx.setAlpha((0.25 + CGFloat(i) * 0.2) * accentAlpha)
        ctx.fillEllipse(in: CGRect(x: p.x - r, y: p.y - r, width: 2 * r, height: 2 * r))
    }
    // Body and head in one transparency layer, so their overlap does not show.
    ctx.setAlpha(accentAlpha)
    ctx.beginTransparencyLayer(auxiliaryInfo: nil)
    let body = CGMutablePath()
    body.move(to: point(0.32))
    for i in 33...92 { body.addLine(to: point(CGFloat(i) / 100)) }
    ctx.addPath(body)
    ctx.setStrokeColor(accent)
    ctx.setLineWidth(5)
    ctx.setLineCap(.round)
    ctx.strokePath()
    // Arrowhead along the curve's final direction.
    let tip = point(1), back = point(0.9)
    let angle = atan2(tip.y - back.y, tip.x - back.x)
    let head = CGMutablePath()
    head.move(to: tip)
    head.addLine(to: CGPoint(x: tip.x - 18 * cos(angle - 0.55), y: tip.y - 18 * sin(angle - 0.55)))
    head.addLine(to: CGPoint(x: tip.x - 18 * cos(angle + 0.55), y: tip.y - 18 * sin(angle + 0.55)))
    head.closeSubpath()
    ctx.addPath(head)
    ctx.fillPath()
    ctx.endTransparencyLayer()
    ctx.setAlpha(1)

    // The instruction, centred near the bottom.
    let text = "Drag Countdown into Applications to install it"
    let font = CTFontCreateUIFontForLanguage(.system, 14, nil)!
    let attributes: [NSAttributedString.Key: Any] = [
        NSAttributedString.Key(kCTFontAttributeName as String): font,
        NSAttributedString.Key(kCTForegroundColorAttributeName as String): color(0x3C4258),
    ]
    let line = CTLineCreateWithAttributedString(NSAttributedString(string: text, attributes: attributes))
    let bounds = CTLineGetBoundsWithOptions(line, .useOpticalBounds)
    ctx.saveGState()
    ctx.textMatrix = CGAffineTransform(scaleX: 1, y: -1)   // undo the flip for glyphs
    ctx.textPosition = CGPoint(x: (width - bounds.width) / 2, y: 330)
    CTLineDraw(line, ctx)
    ctx.restoreGState()
    return ctx.makeImage()!
}

for (name, scale) in [("background.png", CGFloat(1)), ("background@2x.png", CGFloat(2))] {
    let url = URL(fileURLWithPath: outDir).appendingPathComponent(name)
    let dest = CGImageDestinationCreateWithURL(url as CFURL, "public.png" as CFString, 1, nil)!
    CGImageDestinationSetProperties(dest, [kCGImagePropertyDPIWidth: 72 * scale, kCGImagePropertyDPIHeight: 72 * scale] as CFDictionary)
    CGImageDestinationAddImage(dest, render(scale: scale), nil)
    CGImageDestinationFinalize(dest)
    print("wrote \(url.path)")
}
