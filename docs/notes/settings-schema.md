# Defaults schema (extracted from Constants.swift)

| Section | Key | Type | Default |
|---|---|---|---|
| General | updateChannel | `UpdateChannel` | `.stable` |
| General | logLevel | `LogLevel` | `.none` |
| General | menubarIcon | `Bool` | `true` |
| General | showOnAllDisplays | `Bool` | `false` |
| General | automaticallySwitchDisplay | `Bool` | `true` |
| General | releaseName | `String` | `"Kaafu"` |
| General | hideDynamicIslandFromScreenCapture | `Bool` | `false` |
| Behavior | minimumHoverDuration | `TimeInterval` | `0.3` |
| Behavior | enableHaptics | `Bool` | `true` |
| Behavior | openNotchOnHover | `Bool` | `true` |
| Behavior | extendHoverArea | `Bool` | `false` |
| Behavior | externalDisplayStyle | `ExternalDisplayStyle` | `.notch` |
| Behavior | hideNonNotchUntilHover | `Bool` | `false` |
| Behavior | notchHeightMode | `WindowHeightMode` | `WindowHeightMode.matchRealNotchSize` |
| Behavior | nonNotchHeightMode | `WindowHeightMode` | `WindowHeightMode.matchMenuBar` |
| Behavior | nonNotchHeight | `CGFloat` | `32` |
| Behavior | notchHeight | `CGFloat` | `32` |
| Behavior | openNotchWidth | `CGFloat` | `640` |
| Behavior | closedNotchWidth | `CGFloat` | `150` |
| Behavior | customizePhysicalNotchWidth | `Bool` | `false` |
| Behavior | openLastTabByDefault | `Bool` | `false` |
| Appearance | showEmojis | `Bool` | `false` |
| Appearance | alwaysShowTabs | `Bool` | `true` |
| Appearance | showMirror | `Bool` | `false` |
| Appearance | mirrorShape | `MirrorShapeEnum` | `MirrorShapeEnum.rectangle` |
| Appearance | selectedCameraID | `String` | `""` |
| Appearance | settingsIconInNotch | `Bool` | `true` |
| Appearance | lightingEffect | `Bool` | `true` |
| Appearance | accentColor | `Color` | `Color.blue` |
| Appearance | enableShadow | `Bool` | `true` |
| Appearance | cornerRadiusScaling | `Bool` | `true` |
| Appearance | useModernCloseAnimation | `Bool` | `true` |
| Appearance | showNotHumanFace | `Bool` | `false` |
| Appearance | customIdleAnimations | `[CustomIdleAnimation]` | `[]` |
| Appearance | selectedIdleAnimation | `CustomIdleAnimation?` | `nil` |
| Appearance | animationTransformOverrides | `[String: AnimationTransformConfig]` | `[:]` |
| Appearance | tileShowLabels | `Bool` | `false` |
| Appearance | showCalendar | `Bool` | `true` |
| Appearance | hideCompletedReminders | `Bool` | `true` |
| Appearance | hideAllDayEvents | `Bool` | `false` |
| Appearance | sliderUseAlbumArtColor | `SliderColorEnum` | `SliderColorEnum.white` |
| Appearance | playerColorTinting | `Bool` | `true` |
| Appearance | useMusicVisualizer | `Bool` | `true` |
| Appearance | visualizerBarCount | `Int` | `4` |
| Appearance | enableWaveformScrubber | `Bool` | `true` |
| Appearance | colorExtractionMode | `ColorExtractionMode` | `.vibrant` |
| Appearance | customVisualizers | `[CustomVisualizer]` | `[]` |
| Appearance | selectedVisualizer | `CustomVisualizer?` | `nil` |
| Appearance | customAppIcons | `[CustomAppIcon]` | `[]` |
| Appearance | selectedAppIconID | `String?` | `nil` |
| Gestures | enableGestures | `Bool` | `true` |
| Gestures | closeGestureEnabled | `Bool` | `true` |
| Gestures | gestureSensitivity | `CGFloat` | `200.0` |
| Gestures | enableHorizontalMusicGestures | `Bool` | `true` |
| Gestures | musicGestureBehavior | `MusicSkipBehavior` | `.track` |
| Gestures | reverseSwipeGestures | `Bool` | `false` |
| Gestures | reverseScrollGestures | `Bool` | `false` |
| Media playback | coloredSpectrogram | `Bool` | `true` |
| Media playback | enableRealTimeWaveform | `Bool` | `false` |
| Media playback | enableSneakPeek | `Bool` | `false` |
| Media playback | sneakPeekStyles | `SneakPeekStyle` | `.standard` |
| Media playback | showSneakPeekOnTrackChange | `Bool` | `true` |
| Media playback | enableFullscreenMediaDetection | `Bool` | `true` |
| Media playback | parallaxEffectIntensity | `Double` | `6.0` |
| Media playback | waitInterval | `Double` | `3` |
| Media playback | showShuffleAndRepeat | `Bool` | `true` |
| Media playback | showMediaOutputControl | `Bool` | `true` |
| Media playback | alwaysShowLockScreenVolume | `Bool` | `false` |
| Media playback | musicAuxLeftControl | `MusicAuxiliaryControl` | `.shuffle` |
| Media playback | musicAuxRightControl | `MusicAuxiliaryControl` | `.repeatMode` |
| Media playback | didMigrateMusicAuxControls | `Bool` | `false` |
| Media playback | musicControlSlots | `[MusicControlButton]` | `MusicControlButton.defaultLayout` |
| Media playback | didMigrateMusicControlSlots | `Bool` | `false` |
| Media playback | musicSkipBehavior | `MusicSkipBehavior` | `.track` |
| Media playback | musicControlWindowEnabled | `Bool` | `false` |
| Media playback | showStandardMediaControls | `Bool` | `true` |
| Media playback | autoHideInactiveNotchMediaPlayer | `Bool` | `true` |
| Media playback | cachedMusicLiveActivityPreference | `Bool?` | `nil` |
| Media playback | cachedLockScreenMediaWidgetPreference | `Bool?` | `nil` |
| Media playback | cachedMusicControlWindowPreference | `Bool?` | `nil` |
| Media playback | enableLockScreenMediaWidget | `Bool` | `true` |
| Media playback | enableLockScreenWeatherWidget | `Bool` | `true` |
| Media playback | enableLockScreenFocusWidget | `Bool` | `true` |
| Media playback | siriResponsivenessMode | `SiriResponsivenessMode` | `.automatic` |
| Media playback | enableLockScreenReminderWidget | `Bool` | `true` |
| Media playback | enableLockScreenTimerWidget | `Bool` | `true` |
| Media playback | lockScreenWeatherRefreshInterval | `TimeInterval` | `30 * 60` |
| Media playback | lockScreenWeatherShowsLocation | `Bool` | `true` |
| Media playback | lockScreenWeatherShowsSunrise | `Bool` | `true` |
| Media playback | lockScreenWeatherWidgetStyle | `LockScreenWeatherWidgetStyle` | `.inline` |
| Media playback | lockScreenWeatherTemperatureUnit | `LockScreenWeatherTemperatureUnit` | `.matchingSystemPreference` |
| Media playback | lockScreenWeatherShowsAQI | `Bool` | `true` |
| Media playback | lockScreenWeatherAQIScale | `LockScreenWeatherAirQualityScale` | `.us` |
| Media playback | lockScreenWeatherUsesGaugeTint | `Bool` | `false` |
| Media playback | lockScreenWeatherProviderSource | `LockScreenWeatherProviderSource` | `.openMeteo` |
| Media playback | lockScreenWeatherVerticalOffset | `Double` | `0` |
| Media playback | lockScreenMusicVerticalOffset | `Double` | `0` |
| Media playback | lockScreenMusicPanelWidth | `Double` | `Double(LockScreenMusicPanel.defaultCollapsedWidth)` |
| Media playback | lockScreenMusicAlbumParallaxEnabled | `Bool` | `false` |
| Media playback | lockScreenTimerVerticalOffset | `Double` | `0` |
| Media playback | lockScreenTimerWidgetWidth | `Double` | `350` |
| Media playback | lockScreenWidgetAppearance | `LockScreenWidgetAppearance` | `.dark` |
| Media playback | lockScreenGlassStyle | `LockScreenGlassStyle` | `.liquid` |
| Media playback | lockScreenGlassCustomizationMode | `LockScreenGlassCustomizationMode` | `.standard` |
| Media playback | lockScreenTimerGlassStyle | `LockScreenGlassStyle` | `.frosted` |
| Media playback | lockScreenTimerGlassCustomizationMode | `LockScreenGlassCustomizationMode` | `.standard` |
| Media playback | lockScreenMusicLiquidGlassVariant | `LiquidGlassVariant` | `.defaultVariant` |
| Media playback | lockScreenTimerLiquidGlassVariant | `LiquidGlassVariant` | `.defaultVariant` |
| Media playback | lockScreenShowAppIcon | `Bool` | `false` |
| Media playback | lockScreenPanelShowsBorder | `Bool` | `false` |
| Media playback | lockScreenMusicUsesEnhancedLiquidBorder | `Bool` | `true` |
| Media playback | lockScreenPanelUsesBlur | `Bool` | `true` |
| Media playback | lockScreenMusicMergedAirPlayOutput | `Bool` | `true` |
| Media playback | lockScreenMusicFullscreenArtworkEnabled | `Bool` | `true` |
| Media playback | lockScreenKeepAlbumArtVisibleDuringFullscreenArtwork | `Bool` | `false` |
| Media playback | lockScreenMusicFullscreenVideoArtwork | `Bool` | `true` |
| Media playback | lockScreenShowCenteredAlbumArtOverFullscreenCanvas | `Bool` | `true` |
| Media playback | lockScreenTimerWidgetUsesBlur | `Bool` | `false` |
| Media playback | lockScreenReminderChipStyle | `LockScreenReminderChipStyle` | `.eventColor` |
| Media playback | lockScreenReminderWidgetHorizontalAlignment | `String` | `"center"` |
| Media playback | lockScreenReminderWidgetVerticalOffset | `Double` | `0` |
| Media playback | lockScreenShowCalendarEvent | `Bool` | `true` |
| Media playback | lockScreenCalendarEventLookaheadWindow | `String` | `"3h"` |
| Media playback | lockScreenCalendarSelectionMode | `String` | `"all"` |
| Media playback | lockScreenShowCalendarCountdown | `Bool` | `true` |
| Media playback | lockScreenShowCalendarEventEntireDuration | `Bool` | `true` |
| Media playback | lockScreenShowCalendarEventAfterStartEnabled | `Bool` | `false` |
| Media playback | lockScreenShowCalendarEventAfterStartWindow | `String` | `"5m"` |
| Media playback | lockScreenShowCalendarTimeRemaining | `Bool` | `true` |
| Media playback | lockScreenShowCalendarStartTimeAfterBegins | `Bool` | `true` |
| Media playback | lockScreenWeatherWidgetRowOrder | `String` | `"weather_calendar_focus"` |
| Third-party Calendar Integration | enableThirdPartyCalendarApp | `Bool` | `false` |
| Third-party Calendar Integration | selectedCalendarApp | `ThirdPartyCalendarApp` | `.fantastical` |
| Third-party Calendar Integration | fantasticalDefaultView | `FantasticalViewStyle` | `.mini` |
| Battery | showPowerStatusNotifications | `Bool` | `true` |
| Battery | showBatteryIndicator | `Bool` | `BatteryActivityManager.shared.hasBattery()` |
| Battery | showBatteryPercentage | `Bool` | `true` |
| Battery | showBatteryPercentInside | `Bool` | `true` |
| Battery | showMinimalisticBatteryIndicator | `Bool` | `true` |
| Battery | showPowerStatusIcons | `Bool` | `true` |
| Battery | playLowBatteryAlertSound | `Bool` | `true` |
| Battery | showChargingBatteryHUD | `Bool` | `true` |
| Battery | showLowBatteryHUD | `Bool` | `true` |
| Battery | showFullBatteryHUD | `Bool` | `true` |
| Battery | chargingBatteryHUDDuration | `Int` | `3` |
| Battery | lowBatteryHUDDuration | `Int` | `3` |
| Battery | fullBatteryHUDDuration | `Int` | `3` |
| Battery | lowBatteryHUDThreshold | `Int` | `20` |
| Battery | fullBatteryHUDThreshold | `Int` | `100` |
| Battery | lowBatteryHUDStyle | `BatteryNotificationStyle` | `.standard` |
| Battery | fullBatteryHUDStyle | `BatteryNotificationStyle` | `.standard` |
| Battery | lockScreenWeatherShowsBatteryGauge | `Bool` | `BatteryActivityManager.shared.hasBattery()` |
| Battery | lockScreenWeatherBatteryUsesLaptopSymbol | `Bool` | `true` |
| Battery | lockScreenWeatherShowsCharging | `Bool` | `true` |
| Battery | lockScreenWeatherShowsChargingPercentage | `Bool` | `true` |
| Battery | lockScreenWeatherShowsBluetooth | `Bool` | `true` |
| Downloads | enableDownloadListener | `Bool` | `true` |
| Downloads | enableSafariDownloads | `Bool` | `true` |
| Downloads | selectedDownloadIndicatorStyle | `DownloadIndicatorStyle` | `DownloadIndicatorStyle.progress` |
| Downloads | showDownloadSpeed | `Bool` | `false` |
| Downloads | selectedDownloadIconStyle | `DownloadIconStyle` | `DownloadIconStyle.onlyAppIcon` |
| HUD | inlineHUD | `Bool` | `true` |
| HUD | progressBarStyle | `ProgressBarStyle` | `.hierarchical` |
| HUD | enableGradient | `Bool` | `false` |
| HUD | systemEventIndicatorShadow | `Bool` | `false` |
| HUD | systemEventIndicatorUseAccent | `Bool` | `false` |
| HUD | showProgressPercentages | `Bool` | `true` |
| Shelf | dynamicShelf | `Bool` | `true` |
| Shelf | openShelfByDefault | `Bool` | `true` |
| Shelf | quickShareProvider | `String` | `"AirDrop"` |
| Shelf | localSendSelectedDeviceID | `String` | `""` |
| Shelf | localSendDevicePickerGlassMode | `LockScreenGlassCustomizationMode` | `.standard` |
| Shelf | localSendDevicePickerLiquidGlassVariant | `LiquidGlassVariant` | `.v11` |
| Shelf | copyOnDrag | `Bool` | `false` |
| Shelf | allowMoveOnDrag | `Bool` | `false` |
| Shelf | autoRemoveShelfItems | `Bool` | `false` |
| Shelf | expandedDragDetection | `Bool` | `true` |
| Calendar | calendarSelectionState | `CalendarSelectionState` | `.all` |
| Calendar | showFullEventTitles | `Bool` | `false` |
| Calendar | autoScrollToNextEvent | `Bool` | `true` |
| Fullscreen Media Detection | alwaysHideInFullscreen | `Bool` | `false` |
| Fullscreen Media Detection | hideNotchOption | `HideNotchOption` | `.nowPlayingOnly` |
| Wobble Animation | enableWobbleAnimation | `Bool` | `false` |
| Media Controller | mediaController | `MediaControllerType` | `defaultMediaController` |
| Media Controller | spotifySPDCCookie | `String` | `""` |
| Media Controller | spotifyAuthAccessToken | `String` | `""` |
| Media Controller | spotifyAuthAccessTokenExpiration | `Double` | `0` |
| Media Controller | spotifyAuthLastValidatedAt | `Double` | `0` |
| Media Controller | spotifyLibraryClientID | `String` | `""` |
| Media Controller | ciderAPIToken | `String` | `""` |
| Media Controller | spotifyLibraryAccessToken | `String` | `""` |
| Media Controller | spotifyLibraryRefreshToken | `String` | `""` |
| Media Controller | spotifyLibraryTokenExpiration | `Double` | `0` |
| Bluetooth Audio Devices | showBluetoothDeviceConnections | `Bool` | `true` |
| Bluetooth Audio Devices | useColorCodedBatteryDisplay | `Bool` | `true` |
| Bluetooth Audio Devices | useColorCodedVolumeDisplay | `Bool` | `true` |
| Bluetooth Audio Devices | useSmoothColorGradient | `Bool` | `true` |
| Bluetooth Audio Devices | useCircularBluetoothBatteryIndicator | `Bool` | `true` |
| Bluetooth Audio Devices | showBluetoothBatteryPercentageText | `Bool` | `false` |
| Bluetooth Audio Devices | showBluetoothDeviceNameMarquee | `Bool` | `false` |
| Bluetooth Audio Devices | useBluetoothHUD3DIcon | `Bool` | `true` |
| Bluetooth Audio Devices | showAirPodsListeningModeChanges | `Bool` | `true` |
| Stats Feature | enableStatsFeature | `Bool` | `false` |
| Stats Feature | enableLLMUsageFeature | `Bool` | `false` |
| Stats Feature | enableClaudeProvider | `Bool` | `true` |
| Stats Feature | enableCodexProvider | `Bool` | `true` |
| Stats Feature | enableCursorProvider | `Bool` | `true` |
| Stats Feature | enableAntigravityProvider | `Bool` | `true` |
| Stats Feature | enableNewAPIProvider | `Bool` | `false` |
| Stats Feature | newAPIAccounts | `[NewAPIAccount]` | `[]` |
| Stats Feature | autoStartStatsMonitoring | `Bool` | `true` |
| Stats Feature | statsStopWhenNotchCloses | `Bool` | `true` |
| Stats Feature | statsUpdateInterval | `Double` | `1.0` |
| Stats Feature | showCpuGraph | `Bool` | `true` |
| Stats Feature | showMemoryGraph | `Bool` | `true` |
| Stats Feature | showGpuGraph | `Bool` | `true` |
| Stats Feature | showNetworkGraph | `Bool` | `false` |
| Stats Feature | showDiskGraph | `Bool` | `false` |
| Stats Feature | cpuTemperatureUnit | `LockScreenWeatherTemperatureUnit` | `.matchingSystemPreference` |
| Terminal Feature | enableTerminalFeature | `Bool` | `false` |
| Terminal Feature | terminalShellPath | `String` | `"/bin/zsh"` |
| Terminal Feature | terminalFontFamily | `String` | `""` |
| Terminal Feature | terminalFontSize | `Double` | `12.0` |
| Terminal Feature | terminalOpacity | `Double` | `1.0` |
| Terminal Feature | terminalMaxHeightFraction | `Double` | `0.4` |
| Terminal Feature | terminalCursorStyle | `String` | `"blinkBlock"` |
| Terminal Feature | terminalScrollbackLines | `Int` | `1000` |
| Terminal Feature | terminalOptionAsMeta | `Bool` | `true` |
| Terminal Feature | terminalMouseReporting | `Bool` | `true` |
| Terminal Feature | terminalBoldAsBright | `Bool` | `true` |
| Terminal Feature | terminalBackgroundColor | `Color` | `.black` |
| Terminal Feature | terminalForegroundColor | `Color` | `.white` |
| Terminal Feature | terminalCursorColor | `Color` | `Color(.selectedControlColor)` |
| Terminal Feature | terminalStickyMode | `Bool` | `false` |
| Timer Feature | enableTimerFeature | `Bool` | `true` |
| Timer Feature | timerDisplayMode | `TimerDisplayMode` | `.tab` |
| Timer Feature | timerPresets | `[TimerPreset]` | `TimerPreset.defaultPresets` |
| Timer Feature | showTimerPresetsInNotchTab | `Bool` | `true` |
| Timer Feature | timerIconColorMode | `TimerIconColorMode` | `.adaptive` |
| Timer Feature | timerSolidColor | `Color` | `.blue` |
| Timer Feature | timerShowsCountdown | `Bool` | `true` |
| Timer Feature | timerShowsLabel | `Bool` | `false` |
| Timer Feature | timerShowsProgress | `Bool` | `true` |
| Timer Feature | timerProgressStyle | `TimerProgressStyle` | `.bar` |
| Timer Feature | mirrorSystemTimer | `Bool` | `true` |
| Timer Feature | timerInputStyle | `TimerInputStyle` | `.manual` |
| Reminder Live Activity | enableReminderLiveActivity | `Bool` | `true` |
| Reminder Live Activity | reminderPresentationStyle | `ReminderPresentationStyle` | `.ringCountdown` |
| Reminder Live Activity | reminderLeadTime | `Int` | `5` |
| Reminder Live Activity | reminderSneakPeekDuration | `Double` | `5` |
| Reminder Live Activity | timerControlWindowEnabled | `Bool` | `true` |
| Per-App Volume | enablePerAppVolume | `Bool` | `false` |
| Per-App Volume | showPerAppVolumeIcon | `Bool` | `true` |
| Per-App Volume | perAppVolumeLevels | `[String: Double]` | `[:]` |
| Caffeinate Feature | enableCaffeinate | `Bool` | `true` |
| Caffeinate Feature | showCaffeinateIcon | `Bool` | `true` |
| Caffeinate Feature | caffeinateDefaultDuration | `CaffeinateDuration` | `.indefinite` |
| Caffeinate Feature | caffeinateKeepsDisplayAwake | `Bool` | `true` |
| ColorPicker Feature | enableColorPickerFeature | `Bool` | `true` |
| ColorPicker Feature | showColorFormats | `Bool` | `true` |
| ColorPicker Feature | colorPickerDisplayMode | `ColorPickerDisplayMode` | `.panel` |
| ColorPicker Feature | colorHistorySize | `Int` | `10` |
| ColorPicker Feature | showColorPickerIcon | `Bool` | `true` |
| Clipboard Feature | enableClipboardManager | `Bool` | `true` |
| Clipboard Feature | clipboardHistorySize | `Int` | `3` |
| Clipboard Feature | persistClipboardHistory | `Bool` | `true` |
| Clipboard Feature | showClipboardIcon | `Bool` | `true` |
| Clipboard Feature | clipboardDisplayMode | `ClipboardDisplayMode` | `.panel` |
| Screen Assistant Feature | enableScreenAssistant | `Bool` | `true` |
| Screen Assistant Feature | screenAssistantDisplayMode | `ScreenAssistantDisplayMode` | `.panel` |
| Screen Assistant Feature | geminiApiKey | `String` | `""` |
| Screen Assistant Feature | openaiApiKey | `String` | `""` |
| Screen Assistant Feature | claudeApiKey | `String` | `""` |
| Screen Assistant Feature | groqApiKey | `String` | `""` |
| Screen Assistant Feature | selectedAIProvider | `AIModelProvider` | `.gemini` |
| Screen Assistant Feature | selectedAIModel | `AIModel?` | `nil` |
| Screen Assistant Feature | enableThinkingMode | `Bool` | `false` |
| Screen Assistant Feature | localModelEndpoint | `String` | `"http://localhost:11434"` |
| Third-Party Extensions | enableThirdPartyExtensions | `Bool` | `true` |
| Third-Party Extensions | enableExtensionLiveActivities | `Bool` | `true` |
| Third-Party Extensions | enableExtensionLockScreenWidgets | `Bool` | `true` |
| Third-Party Extensions | enableExtensionNotchExperiences | `Bool` | `true` |
| Third-Party Extensions | enableExtensionNotchTabs | `Bool` | `true` |
| Third-Party Extensions | enableExtensionNotchMinimalisticOverrides | `Bool` | `true` |
| Third-Party Extensions | enableExtensionNotchInteractiveWebViews | `Bool` | `true` |
| Third-Party Extensions | extensionAuthorizationEntries | `[ExtensionAuthorizationEntry]` | `[]` |
| Third-Party Extensions | extensionRateLimitRecords | `[ExtensionRateLimitRecord]` | `[]` |
| Third-Party Extensions | extensionDiagnosticsLoggingEnabled | `Bool` | `true` |
| Third-Party Extensions | extensionLiveActivityCapacity | `Int` | `4` |
| Third-Party Extensions | extensionLockScreenWidgetCapacity | `Int` | `4` |
| Third-Party Extensions | extensionNotchExperienceCapacity | `Int` | `2` |
| Third-Party Extensions | enableExtensionFileSharing | `Bool` | `true` |
| Keyboard Shortcuts | enableShortcuts | `Bool` | `true` |
| System HUD Feature | enableSystemHUD | `Bool` | `true` |
| System HUD Feature | enableVolumeHUD | `Bool` | `true` |
| System HUD Feature | enableBrightnessHUD | `Bool` | `true` |
| System HUD Feature | enableKeyboardBacklightHUD | `Bool` | `true` |
| System HUD Feature | systemHUDSensitivity | `Int` | `5` |
| System HUD Feature | playVolumeChangeFeedback | `Bool` | `false` |
| System HUD Feature | volumeStepPercent | `Int` | `6` |
| System HUD Feature | volumeFineStepPercent | `Int` | `2` |
| System HUD Feature | brightnessStepPercent | `Int` | `6` |
| System HUD Feature | brightnessFineStepPercent | `Int` | `2` |
| Custom OSD Window Feature | enableCustomOSD | `Bool` | `false` |
| Custom OSD Window Feature | enableVerticalHUD | `Bool` | `false` |
| Custom OSD Window Feature | enableCircularHUD | `Bool` | `false` |
| Custom OSD Window Feature | verticalHUDPosition | `String` | `"right"` |
| Custom OSD Window Feature | verticalHUDShowValue | `Bool` | `true` |
| Custom OSD Window Feature | verticalHUDInteractive | `Bool` | `true` |
| Custom OSD Window Feature | verticalHUDHeight | `CGFloat` | `160` |
| Custom OSD Window Feature | verticalHUDWidth | `CGFloat` | `36` |
| Custom OSD Window Feature | verticalHUDPadding | `CGFloat` | `24` |
| Custom OSD Window Feature | verticalHUDUseAccentColor | `Bool` | `false` |
| Custom OSD Window Feature | verticalHUDMaterial | `OSDMaterial` | `.frosted` |
| Custom OSD Window Feature | verticalHUDLiquidGlassCustomizationMode | `LockScreenGlassCustomizationMode` | `.standard` |
| Custom OSD Window Feature | verticalHUDLiquidGlassVariant | `LiquidGlassVariant` | `.defaultVariant` |
| Custom OSD Window Feature | circularHUDShowValue | `Bool` | `true` |
| Custom OSD Window Feature | circularHUDSize | `CGFloat` | `65` |
| Custom OSD Window Feature | circularHUDStrokeWidth | `CGFloat` | `4` |
| Custom OSD Window Feature | circularHUDUseAccentColor | `Bool` | `true` |
| Third-Party DDC Integration | enableThirdPartyDDCIntegration | `Bool` | `false` |
| Third-Party DDC Integration | thirdPartyDDCProvider | `ThirdPartyDDCProvider` | `.betterDisplay` |
| Third-Party DDC Integration | enableExternalVolumeControlListener | `Bool` | `false` |
| Third-Party DDC Integration | didMigrateThirdPartyDDCIntegration | `Bool` | `false` |
| Third-Party DDC Integration | enableBetterDisplayIntegration | `Bool` | `false` |
| Third-Party DDC Integration | enableLunarIntegration | `Bool` | `false` |
| Third-Party DDC Integration | hasSeenOSDAlphaWarning | `Bool` | `false` |
| Third-Party DDC Integration | enableOSDVolume | `Bool` | `true` |
| Third-Party DDC Integration | enableOSDBrightness | `Bool` | `true` |
| Third-Party DDC Integration | enableOSDKeyboardBacklight | `Bool` | `true` |
| Third-Party DDC Integration | osdMaterial | `OSDMaterial` | `.frosted` |
| Third-Party DDC Integration | osdLiquidGlassCustomizationMode | `LockScreenGlassCustomizationMode` | `.standard` |
| Third-Party DDC Integration | osdLiquidGlassVariant | `LiquidGlassVariant` | `.defaultVariant` |
| Third-Party DDC Integration | osdIconColorStyle | `OSDIconColorStyle` | `.white` |
| Screen Recording Detection Feature | enableScreenRecordingDetection | `Bool` | `true` |
| Screen Recording Detection Feature | showRecordingIndicator | `Bool` | `true` |
| Screen Recording Detection Feature | recordingHoverStyle | `RecordingHoverStyle` | `.default` |
| Screen Recording Detection Feature | recordingControlMode | `RecordingControlMode` | `.withStopButton` |
| Screen Recording Detection Feature | enableScreenRecordingPolling | `Bool` | `false` |
| Focus / Do Not Disturb Detection | enableDoNotDisturbDetection | `Bool` | `true` |
| Focus / Do Not Disturb Detection | focusMonitoringMode | `FocusMonitoringMode` | `.withoutDevTools` |
| Focus / Do Not Disturb Detection | showDoNotDisturbIndicator | `Bool` | `true` |
| Focus / Do Not Disturb Detection | showDoNotDisturbLabel | `Bool` | `true` |
| Focus / Do Not Disturb Detection | focusIndicatorNonPersistent | `Bool` | `false` |
| Privacy Indicators (Camera & Microphone Detection) | enableCameraDetection | `Bool` | `true` |
| Privacy Indicators (Camera & Microphone Detection) | enableMicrophoneDetection | `Bool` | `true` |
| Lock Screen Features | enableLockScreenLiveActivity | `Bool` | `true` |
| Lock Screen Features | lockScreenLiveActivityIconStyle | `LockScreenLiveActivityIconStyle` | `.lock` |
| Lock Screen Features | enableLockSounds | `Bool` | `true` |
| Caps Lock Indicator | enableCapsLockIndicator | `Bool` | `true` |
| Caps Lock Indicator | capsLockIndicatorUseGreenColor | `Bool` | `false` |
| Caps Lock Indicator | capsLockIndicatorTintMode | `CapsLockIndicatorTintMode` | `.white` |
| Caps Lock Indicator | didMigrateCapsLockTintMode | `Bool` | `false` |
| Caps Lock Indicator | didMigrateClipboardShortcutToV | `Bool` | `false` |
| Caps Lock Indicator | showCapsLockLabel | `Bool` | `false` |
| ImageService | didClearLegacyURLCacheV1 | `Bool` | `false` |
| Minimalistic UI Mode | enableMinimalisticUI | `Bool` | `false` |
| Lyrics Feature | enableLyrics | `Bool` | `false` |
| Lyrics Feature | lyricHighlightStyle | `LyricHighlightStyle` | `.sweep` |
| Lyrics Feature | pinnedLyricContext | `PinnedLyricContext` | `.current` |
| Lyrics Feature | pinLyricsWhenClosed | `Bool` | `false` |
| Lyrics Feature | lyricsPanelWidth | `CGFloat` | `280` |
| Lyrics Feature | lyricsPanelOffset | `CGFloat` | `0` |
| Lyrics Feature | showLiveCanvasInDynamicIsland | `Bool` | `false` |
| Notes Feature | enableNotes | `Bool` | `false` |
| Notes Feature | enableNotePinning | `Bool` | `true` |
| Notes Feature | enableNoteSearch | `Bool` | `false` |
| Notes Feature | enableNoteColorFiltering | `Bool` | `false` |
| Notes Feature | enableCreateFromClipboard | `Bool` | `true` |
| Notes Feature | enableNoteCharCount | `Bool` | `true` |
| Notes Feature | savedNotes | `[NoteItem]` | `[]` |
| Notes Feature | enableAppleNotesSync | `Bool` | `false` |
| Notes Feature | appleNotesLastSyncDate | `Date?` | `nil` |
| Notes Feature | showSongMetadataInClosedNotch | `Bool` | `false` |
