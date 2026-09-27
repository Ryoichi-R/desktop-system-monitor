import CoreGraphics

public struct DisplayFullScreenState {
  private var states: [Int: Bool] = [:]

  public init() {}

  public func isFullScreen(displayID: Int) -> Bool {
    states[displayID] ?? false
  }

  @discardableResult
  public mutating func update(displayID: Int, isFullScreen: Bool) -> Bool? {
    let previous = states[displayID]
    states[displayID] = isFullScreen
    return previous
  }

  public mutating func remove(displayID: Int) {
    states.removeValue(forKey: displayID)
  }

  public mutating func failOpen(displayIDs: [Int]) -> [Int] {
    var restoredDisplayIDs: [Int] = []
    for displayID in displayIDs {
      if states[displayID] == true {
        restoredDisplayIDs.append(displayID)
      }
      states[displayID] = false
    }
    return restoredDisplayIDs
  }
}

public enum FullScreenWindowPolicy {
  public static func coversDisplay(
    windowBounds: CGRect,
    displayBounds: CGRect,
    minimumCoverage: CGFloat = 0.95
  ) -> Bool {
    guard minimumCoverage.isFinite, (0...1).contains(minimumCoverage),
      isUsable(windowBounds), isUsable(displayBounds)
    else {
      return false
    }

    let displayArea = displayBounds.width * displayBounds.height
    guard displayArea.isFinite, displayArea > 0 else {
      return false
    }

    let overlap = windowBounds.intersection(displayBounds)
    guard !overlap.isNull, !overlap.isEmpty else {
      return false
    }
    let overlapArea = overlap.width * overlap.height
    guard overlapArea.isFinite else {
      return false
    }
    return overlapArea / displayArea >= minimumCoverage
  }

  private static func isUsable(_ rect: CGRect) -> Bool {
    rect.origin.x.isFinite && rect.origin.y.isFinite
      && rect.width.isFinite && rect.height.isFinite
      && rect.width > 0 && rect.height > 0
  }
}
