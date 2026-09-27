import CoreGraphics
import DisplayProbePolicy

@main
private struct DisplayProbePolicyChecks {
  static func main() {
    checkPerDisplayCoverageAndThreshold()
    checkInvalidBoundsAndThresholds()
    checkFailOpenRecovery()
    checkRemovedDisplayRecovery()
    print("Passed 4 display policy checks")
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
