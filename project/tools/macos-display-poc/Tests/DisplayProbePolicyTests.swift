import CoreGraphics
import DisplayProbePolicy

@main
private struct DisplayProbePolicyChecks {
  static func main() {
    checkPerDisplayCoverageAndThreshold()
    checkInvalidBoundsAndThresholds()
    checkFailOpenRecovery()
    checkRemovedDisplayRecovery()
    checkIndependentDisplayFocus()
    checkOccludedWindowAndOwnPanel()
    checkFilteredCandidatesAndEmptySpace()
    checkMaximizedHeuristic()
    print("Passed 8 display policy checks")
  }

  private static func checkIndependentDisplayFocus() {
    let left = CGRect(x: -1000, y: 0, width: 1000, height: 800)
    let right = CGRect(x: 0, y: 0, width: 1000, height: 800)
    let fullLeft = DisplayWindowCandidate(ownerPID: 1, layer: 0, bounds: left)
    let normalRight = DisplayWindowCandidate(
      ownerPID: 2, layer: 0, bounds: CGRect(x: 50, y: 50, width: 500, height: 400))
    for windows in [[fullLeft, normalRight], [normalRight, fullLeft]] {
      precondition(FullScreenWindowPolicy.isFullScreen(
        windowsFrontToBack: windows, displayBounds: left, excludedOwnerPID: 99))
      precondition(!FullScreenWindowPolicy.isFullScreen(
        windowsFrontToBack: windows, displayBounds: right, excludedOwnerPID: 99))
    }
    let fullRight = DisplayWindowCandidate(ownerPID: 2, layer: 0, bounds: right)
    for display in [left, right] {
      precondition(FullScreenWindowPolicy.isFullScreen(
        windowsFrontToBack: [fullRight, fullLeft], displayBounds: display, excludedOwnerPID: 99))
    }
  }

  private static func checkOccludedWindowAndOwnPanel() {
    let display = CGRect(x: 0, y: 0, width: 1000, height: 800)
    let background = DisplayWindowCandidate(ownerPID: 1, layer: 0, bounds: display)
    let normal = DisplayWindowCandidate(
      ownerPID: 2, layer: 0, bounds: CGRect(x: 50, y: 50, width: 500, height: 400))
    precondition(!FullScreenWindowPolicy.isFullScreen(
      windowsFrontToBack: [normal, background], displayBounds: display, excludedOwnerPID: 99))
    let ownPanel = DisplayWindowCandidate(ownerPID: 99, layer: 0, bounds: normal.bounds)
    precondition(FullScreenWindowPolicy.isFullScreen(
      windowsFrontToBack: [ownPanel, background], displayBounds: display, excludedOwnerPID: 99))
  }

  private static func checkFilteredCandidatesAndEmptySpace() {
    let display = CGRect(x: 0, y: 0, width: 1000, height: 800)
    let ignored = [
      DisplayWindowCandidate(ownerPID: 1, layer: 1, bounds: display),
      DisplayWindowCandidate(ownerPID: 1, layer: 0, bounds: display, alpha: 0),
      DisplayWindowCandidate(ownerPID: 1, layer: 0, bounds: display, alpha: .nan),
      DisplayWindowCandidate(ownerPID: 1, layer: 0, bounds: .zero),
    ]
    precondition(!FullScreenWindowPolicy.isFullScreen(
      windowsFrontToBack: ignored, displayBounds: display, excludedOwnerPID: 99))
    let full = DisplayWindowCandidate(ownerPID: 2, layer: 0, bounds: display)
    precondition(FullScreenWindowPolicy.isFullScreen(
      windowsFrontToBack: ignored + [full], displayBounds: display, excludedOwnerPID: 99))
    precondition(!FullScreenWindowPolicy.isFullScreen(
      windowsFrontToBack: [], displayBounds: display, excludedOwnerPID: 99))
    precondition(!FullScreenWindowPolicy.isFullScreen(
      windowsFrontToBack: [full], displayBounds: .zero, excludedOwnerPID: 99))
  }

  private static func checkMaximizedHeuristic() {
    let display = CGRect(x: 0, y: 0, width: 1000, height: 800)
    // This remains a coverage heuristic; maximized windows may also match.
    let maximized = DisplayWindowCandidate(ownerPID: 1, layer: 0, bounds: display)
    precondition(FullScreenWindowPolicy.isFullScreen(
      windowsFrontToBack: [maximized], displayBounds: display, excludedOwnerPID: 99))
  }

  private static func checkPerDisplayCoverageAndThreshold() {
    let leftDisplay = CGRect(x: -1000, y: 0, width: 1000, height: 800)
    let rightDisplay = CGRect(x: 0, y: 0, width: 1000, height: 800)
    precondition(
      FullScreenWindowPolicy.coversDisplay(
        windowBounds: leftDisplay,
        displayBounds: leftDisplay),
      "A window covering its display must match")
    precondition(
      !FullScreenWindowPolicy.coversDisplay(
        windowBounds: rightDisplay,
        displayBounds: leftDisplay),
      "A window on another display must not match")
    precondition(
      FullScreenWindowPolicy.coversDisplay(
        windowBounds: CGRect(x: 0, y: 0, width: 950, height: 800),
        displayBounds: rightDisplay),
      "Coverage at the 95% threshold must match")
    precondition(
      !FullScreenWindowPolicy.coversDisplay(
        windowBounds: CGRect(x: 0, y: 0, width: 949, height: 800),
        displayBounds: rightDisplay),
      "Coverage below the 95% threshold must not match")
  }

  private static func checkInvalidBoundsAndThresholds() {
    let display = CGRect(x: 0, y: 0, width: 1000, height: 800)
    precondition(
      !FullScreenWindowPolicy.coversDisplay(
        windowBounds: .zero,
        displayBounds: display),
      "Zero-sized windows must not match")
    precondition(
      !FullScreenWindowPolicy.coversDisplay(
        windowBounds: display,
        displayBounds: display,
        minimumCoverage: .infinity),
      "Non-finite thresholds must not match")
  }

  private static func checkFailOpenRecovery() {
    var state = DisplayFullScreenState()
    state.update(displayID: 41, isFullScreen: true)
    state.update(displayID: 42, isFullScreen: false)

    precondition(state.failOpen(displayIDs: [41, 42]) == [41])
    precondition(!state.isFullScreen(displayID: 41))
    precondition(!state.isFullScreen(displayID: 42))
    precondition(state.failOpen(displayIDs: [41, 42]).isEmpty)
  }

  private static func checkRemovedDisplayRecovery() {
    var state = DisplayFullScreenState()
    state.update(displayID: 51, isFullScreen: true)
    state.remove(displayID: 51)

    precondition(state.failOpen(displayIDs: [51]).isEmpty)
    precondition(!state.isFullScreen(displayID: 51))
  }
}
