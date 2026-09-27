// swift-tools-version: 5.9
import PackageDescription

let package = Package(
  name: "MacOSDisplayPoC",
  platforms: [.macOS(.v14)],
  products: [
    .executable(name: "macos-display-poc", targets: ["DisplayProbe"]),
    .executable(name: "display-policy-checks", targets: ["DisplayProbePolicyChecks"]),
  ],
  targets: [
    .target(
      name: "DisplayProbePolicy",
      path: "Sources/DisplayProbePolicy"),
    .executableTarget(
      name: "DisplayProbe",
      dependencies: ["DisplayProbePolicy"],
      path: ".",
      exclude: ["Package.swift", "README.md", "Sources", "Tests"],
      sources: ["main.swift"]),
    .executableTarget(
      name: "DisplayProbePolicyChecks",
      dependencies: ["DisplayProbePolicy"],
      path: "Tests"),
  ],
  swiftLanguageVersions: [.v5])
