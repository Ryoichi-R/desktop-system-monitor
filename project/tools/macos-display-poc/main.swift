import AppKit
import CoreGraphics
import Foundation

private struct DisplayDescriptor {
  let id: CGDirectDisplayID
  let frame: CGRect
  let visibleFrame: CGRect
  let quartzBounds: CGRect
  let scale: CGFloat
  let isPrimary: Bool

  var summary: [String: Any] {
    [
      "displayID": Int(id),
      "framePoints": Self.rectangle(frame),
      "visibleFramePoints": Self.rectangle(visibleFrame),
      "quartzBounds": Self.rectangle(quartzBounds),
      "backingScale": Double(scale),
      "primary": isPrimary,
    ]
  }

  private static func rectangle(_ rect: CGRect) -> [String: Double] {
    [
      "x": Double(rect.origin.x),
      "y": Double(rect.origin.y),
      "width": Double(rect.width),
      "height": Double(rect.height),
    ]
  }
}

private struct NormalizedAnchor {
  var x: CGFloat = 0.04
  var y: CGFloat = 0.72
}

private final class DisplayProbeOverlayView: NSView {
  var displayID: CGDirectDisplayID = 0
  var scale: CGFloat = 1
  var isPrimary = false
  var hiddenForFullScreen = false

  override func draw(_ dirtyRect: NSRect) {
    NSColor.systemIndigo.withAlphaComponent(0.9).setFill()
    NSBezierPath(roundedRect: bounds, xRadius: 12, yRadius: 12).fill()

    let lines = [
      "P0-2b Display \(displayID)\(isPrimary ? " · primary" : "")",
      "Backing scale: \(String(format: "%.2f", scale))",
      hiddenForFullScreen ? "Full-screen detected · hidden" : "Overlay visible",
    ]
    let attributes: [NSAttributedString.Key: Any] = [
      .font: NSFont.systemFont(ofSize: 14, weight: .medium),
      .foregroundColor: NSColor.white,
    ]
    for (index, line) in lines.enumerated() {
      let y = bounds.height - CGFloat(28 + (index * 24))
      line.draw(at: NSPoint(x: 16, y: y), withAttributes: attributes)
    }
  }
}

@MainActor
private final class DisplayProbeAppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
  private let panelSize = NSSize(width: 340, height: 104)
  private let dashboardWindow = NSWindow(
    contentRect: NSRect(x: 0, y: 0, width: 620, height: 420),
    styleMask: [.titled, .closable, .miniaturizable, .resizable],
    backing: .buffered,
    defer: false)
  private let logView = NSTextView()
  private var panels: [CGDirectDisplayID: NSPanel] = [:]
  private var panelViews: [CGDirectDisplayID: DisplayProbeOverlayView] = [:]
  private var anchors: [CGDirectDisplayID: NormalizedAnchor] = [:]
  private var lastFullScreenState: [CGDirectDisplayID: Bool] = [:]
  private var notificationTokens: [NSObjectProtocol] = []
  private var sampleTimer: Timer?
  private var detectorUnavailableWasLogged = false

  func applicationDidFinishLaunching(_ notification: Notification) {
    configureDashboard()
    installObservers()
    synchronizeDisplays(reason: "started")
    sampleWindowState()

    sampleTimer = Timer.scheduledTimer(
      timeInterval: 1,
      target: self,
      selector: #selector(sampleWindowState),
      userInfo: nil,
      repeats: true)

    dashboardWindow.center()
    dashboardWindow.makeKeyAndOrderFront(nil)
    NSApp.activate(ignoringOtherApps: true)
    logEvent("applicationStarted", fields: ["pollIntervalSeconds": 1])
  }

  func applicationWillTerminate(_ notification: Notification) {
    sampleTimer?.invalidate()
    sampleTimer = nil
    for token in notificationTokens {
      NotificationCenter.default.removeObserver(token)
      NSWorkspace.shared.notificationCenter.removeObserver(token)
    }
    notificationTokens.removeAll()
  }

  func windowDidMove(_ notification: Notification) {
    guard let window = notification.object as? NSWindow,
      let id = panels.first(where: { $0.value === window })?.key,
      let display = displays().first(where: { $0.id == id })
    else {
      return
    }

    let availableWidth = max(1, display.visibleFrame.width - window.frame.width)
    let availableHeight = max(1, display.visibleFrame.height - window.frame.height)
    anchors[id] = NormalizedAnchor(
      x: min(1, max(0, (window.frame.minX - display.visibleFrame.minX) / availableWidth)),
      y: min(1, max(0, (window.frame.minY - display.visibleFrame.minY) / availableHeight)))
  }

  private func configureDashboard() {
    dashboardWindow.title = "Desktop System Monitor · P0-2b Display Probe"
    dashboardWindow.isReleasedWhenClosed = false
    dashboardWindow.minSize = NSSize(width: 480, height: 260)

    let content = NSView(frame: NSRect(x: 0, y: 0, width: 620, height: 420))
    let instructions = NSTextField(
      wrappingLabelWithString:
        "各ディスプレイに表示する紫色のパネルで、Space・全画面・切断／再接続・倍率変更の挙動を確認します。ログにウィンドウ名、アプリ名、PIDは記録しません。")
    instructions.frame = NSRect(x: 16, y: 372, width: 588, height: 36)
    instructions.lineBreakMode = .byWordWrapping
    instructions.maximumNumberOfLines = 2
    content.addSubview(instructions)

    let scrollView = NSScrollView(frame: NSRect(x: 16, y: 16, width: 588, height: 344))
    scrollView.hasVerticalScroller = true
    scrollView.autohidesScrollers = true
    scrollView.borderType = .bezelBorder

    logView.frame = scrollView.bounds
    logView.isEditable = false
    logView.isSelectable = true
    logView.font = NSFont.monospacedSystemFont(ofSize: 11, weight: .regular)
    logView.autoresizingMask = [.width]
    scrollView.documentView = logView
    content.addSubview(scrollView)

    dashboardWindow.contentView = content
  }

  private func installObservers() {
    let displayToken = NotificationCenter.default.addObserver(
      forName: NSApplication.didChangeScreenParametersNotification,
      object: NSApp,
      queue: .main
    ) { [weak self] _ in
      Task { @MainActor in
        self?.synchronizeDisplays(reason: "displayParametersChanged")
        self?.sampleWindowState()
      }
    }
    notificationTokens.append(displayToken)

    let spaceToken = NSWorkspace.shared.notificationCenter.addObserver(
      forName: NSWorkspace.activeSpaceDidChangeNotification,
      object: NSWorkspace.shared,
      queue: .main
    ) { [weak self] _ in
      Task { @MainActor in
        self?.logEvent("activeSpaceChanged")
        self?.sampleWindowState()
      }
    }
    notificationTokens.append(spaceToken)
  }

  private func displays() -> [DisplayDescriptor] {
    let key = NSDeviceDescriptionKey("NSScreenNumber")
    return NSScreen.screens.compactMap { screen in
      guard let number = screen.deviceDescription[key] as? NSNumber else {
        return nil
      }
      let id = CGDirectDisplayID(number.uint32Value)
      return DisplayDescriptor(
        id: id,
        frame: screen.frame,
        visibleFrame: screen.visibleFrame,
        quartzBounds: CGDisplayBounds(id),
        scale: screen.backingScaleFactor,
        isPrimary: CGDisplayIsMain(id) != 0)
    }.sorted { $0.id < $1.id }
  }

  private func synchronizeDisplays(reason: String) {
    let currentDisplays = displays()
    let currentIDs = Set(currentDisplays.map(\.id))
    let removedIDs = panels.keys.filter { !currentIDs.contains($0) }
    for id in removedIDs {
      panels[id]?.orderOut(nil)
      panels[id]?.close()
      panels.removeValue(forKey: id)
      panelViews.removeValue(forKey: id)
      lastFullScreenState.removeValue(forKey: id)
    }

    for display in currentDisplays {
      if panels[display.id] == nil {
        createPanel(for: display)
      }
      placePanel(for: display)
      refreshOverlayLabel(for: display, fullScreen: lastFullScreenState[display.id] ?? false)
    }

    detectorUnavailableWasLogged = false
    let entries = currentDisplays.map(\.summary)
    logEvent(
      "displayTopologyChanged",
      fields: [
        "reason": reason,
        "displayCount": currentDisplays.count,
        "displays": entries,
      ])
  }

  private func createPanel(for display: DisplayDescriptor) {
    let panel = NSPanel(
      contentRect: NSRect(origin: .zero, size: panelSize),
      styleMask: [.borderless, .nonactivatingPanel],
      backing: .buffered,
      defer: false)
    panel.isReleasedWhenClosed = false
    panel.isOpaque = false
    panel.backgroundColor = .clear
    panel.hasShadow = true
    panel.hidesOnDeactivate = false
    panel.isMovableByWindowBackground = true
    panel.level = .floating
    panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]

    let overlayView = DisplayProbeOverlayView(frame: NSRect(origin: .zero, size: panelSize))
    overlayView.displayID = display.id
    overlayView.scale = display.scale
    overlayView.isPrimary = display.isPrimary
    panel.contentView = overlayView
    panel.delegate = self

    panels[display.id] = panel
    panelViews[display.id] = overlayView
    anchors[display.id] = anchors[display.id] ?? NormalizedAnchor()
    panel.orderFrontRegardless()
  }

  private func placePanel(for display: DisplayDescriptor) {
    guard let panel = panels[display.id] else {
      return
    }
    let anchor = anchors[display.id] ?? NormalizedAnchor()
    let availableWidth = max(0, display.visibleFrame.width - panel.frame.width)
    let availableHeight = max(0, display.visibleFrame.height - panel.frame.height)
    let origin = NSPoint(
      x: display.visibleFrame.minX + (availableWidth * anchor.x),
      y: display.visibleFrame.minY + (availableHeight * anchor.y))
    panel.setFrameOrigin(origin)
  }

  private func refreshOverlayLabel(for display: DisplayDescriptor, fullScreen: Bool) {
    guard let view = panelViews[display.id] else {
      return
    }
    view.displayID = display.id
    view.scale = display.scale
    view.isPrimary = display.isPrimary
    view.hiddenForFullScreen = fullScreen
    view.needsDisplay = true
  }

  @objc private func sampleWindowState() {
    let currentDisplays = displays()
    guard let fullScreenIDs = detectFullScreenDisplays(for: currentDisplays) else {
      if !detectorUnavailableWasLogged {
        logEvent("fullScreenDetectorUnavailable")
        detectorUnavailableWasLogged = true
      }
      return
    }
    detectorUnavailableWasLogged = false

    for display in currentDisplays {
      let detected = fullScreenIDs.contains(display.id)
      let wasDetected = lastFullScreenState[display.id]
      lastFullScreenState[display.id] = detected
      refreshOverlayLabel(for: display, fullScreen: detected)

      guard wasDetected != detected, let panel = panels[display.id] else {
        continue
      }
      if detected {
        panel.orderOut(nil)
      } else {
        panel.orderFrontRegardless()
      }
      logEvent(
        "fullScreenStateChanged",
        fields: [
          "displayID": Int(display.id),
          "detected": detected,
          "overlayVisible": !detected,
        ])
    }
  }

  private func detectFullScreenDisplays(
    for currentDisplays: [DisplayDescriptor]
  ) -> Set<CGDirectDisplayID>? {
    guard let frontmost = NSWorkspace.shared.frontmostApplication else {
      return nil
    }
    guard frontmost.processIdentifier != ProcessInfo.processInfo.processIdentifier else {
      return []
    }
    guard
      let windowInfos = CGWindowListCopyWindowInfo(
        [.optionOnScreenOnly, .excludeDesktopElements],
        kCGNullWindowID) as? [[String: Any]]
    else {
      return nil
    }

    let ownerKey = kCGWindowOwnerPID as String
    let layerKey = kCGWindowLayer as String
    let boundsKey = kCGWindowBounds as String
    let frontmostPID = frontmost.processIdentifier
    let candidateWindows: [CGRect] = windowInfos.compactMap { info in
      guard (info[ownerKey] as? NSNumber)?.int32Value == frontmostPID,
        (info[layerKey] as? NSNumber)?.intValue == 0,
        let dictionary = info[boundsKey] as? NSDictionary
      else {
        return nil
      }
      var bounds = CGRect.zero
      guard CGRectMakeWithDictionaryRepresentation(dictionary as CFDictionary, &bounds) else {
        return nil
      }
      return bounds
    }

    var result = Set<CGDirectDisplayID>()
    for display in currentDisplays {
      let displayArea = display.quartzBounds.width * display.quartzBounds.height
      guard displayArea > 0 else {
        continue
      }
      let coversDisplay = candidateWindows.contains { windowBounds in
        let overlap = windowBounds.intersection(display.quartzBounds)
        guard !overlap.isNull, !overlap.isEmpty else {
          return false
        }
        let overlapArea = overlap.width * overlap.height
        return overlapArea / displayArea >= 0.95
      }
      if coversDisplay {
        result.insert(display.id)
      }
    }
    return result
  }

  private func logEvent(_ event: String, fields: [String: Any] = [:]) {
    var payload: [String: Any] = [
      "timestamp": ISO8601DateFormatter().string(from: Date()),
      "event": event,
    ]
    for (key, value) in fields {
      payload[key] = value
    }
    guard let data = try? JSONSerialization.data(withJSONObject: payload, options: [.sortedKeys]),
      let line = String(data: data, encoding: .utf8)
    else {
      return
    }

    print(line)
    logView.textStorage?.append(NSAttributedString(string: line + "\n"))
    logView.scrollToEndOfDocument(nil)
  }
}

@main
private struct DisplayProbeMain {
  @MainActor
  static func main() {
    let application = NSApplication.shared
    application.setActivationPolicy(.accessory)
    let delegate = DisplayProbeAppDelegate()
    application.delegate = delegate
    application.run()
  }
}
