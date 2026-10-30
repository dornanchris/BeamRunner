// BeamRunner: minimal native bridge to Game Center (GameKit) and the iCloud key-value store.
//
// Called from C# (Assets/BeamRunner/Scripts/Runtime/Services/Online/GameCenterNative.cs) with [DllImport("__Internal")]
// on the Unity main thread, which is the app's main thread on iOS. No callbacks into C#: GameKit results are written to
// a few static fields (on the main queue) that C# polls, so there is no UnitySendMessage / reverse P/Invoke to get wrong.
// Strings returned to C# are malloc'ed copies (the IL2CPP marshaller frees them). Booleans are returned as int.
//
// Only APIs available on the deployment target (iOS 15) and not deprecated in the iOS 26/27 SDKs are used:
//   GKLocalPlayer.authenticateHandler (iOS 6), GKLeaderboard submitScore:context:player:leaderboardIDs: (iOS 14),
//   GKAchievement reportAchievements: (iOS 6), GKAccessPoint triggerAccessPointWithState:handler: (iOS 14; replaces
//   the GKGameCenterViewController deprecated in iOS 26), NSUbiquitousKeyValueStore (iOS 5).
// Compiled with ARC (-fobjc-arc, see the .meta); GameKit.framework is linked through the plugin's framework dependency.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <GameKit/GameKit.h>
#include <stdlib.h>
#include <string.h>

#if !__has_feature(objc_arc)
#error "FieldGameCenter.m must be compiled with ARC (-fobjc-arc)."
#endif

// Exported to C# (GameCenterNative.cs); declared here so -Wmissing-prototypes stays quiet.
void FieldGC_Authenticate(void);
int FieldGC_AuthState(void);
int FieldGC_AuthChangeCount(void);
long long FieldGC_LastErrorCode(void);
int FieldGC_PresentLogin(void);
char *FieldGC_PlayerName(void);
char *FieldGC_PlayerId(void);
void FieldGC_SubmitScore(const char *leaderboardId, long long score, long long context);
void FieldGC_ReportAchievement(const char *achievementId, int showBanner);
int FieldGC_ShowDashboard(int state);
int FieldKvs_Synchronize(void);
char *FieldKvs_GetString(const char *key);
int FieldKvs_SetString(const char *key, const char *value);
int FieldKvs_ChangeCount(void);
int FieldKvs_AccountChangeCount(void);

// Auth states shared with GameCenterNative.cs.
enum
{
    FieldGCStateIdle = 0,           // Authenticate not called yet
    FieldGCStateAuthenticating = 1, // waiting for GameKit
    FieldGCStateAuthenticated = 2,
    FieldGCStateSignedOut = 3,      // declined / cancelled / restricted / error
    FieldGCStateLoginAvailable = 4, // GameKit offered its sign-in view controller (FieldGC_PresentLogin shows it)
};

static int s_fieldAuthState = FieldGCStateIdle;
static int s_fieldAuthChanges = 0;
static long s_fieldLastErrorCode = 0;
static UIViewController *s_fieldLoginController = nil;

static int s_fieldKvsChanges = 0;
static int s_fieldKvsAccountChanges = 0;
static id s_fieldKvsObserver = nil;

static char *FieldCopyString(NSString *value)
{
    if (value == nil)
    {
        return NULL;
    }

    const char *utf8 = [value UTF8String];
    if (utf8 == NULL)
    {
        return NULL;
    }

    size_t length = strlen(utf8) + 1;
    char *copy = (char *)malloc(length);
    if (copy != NULL)
    {
        memcpy(copy, utf8, length);
    }

    return copy;
}

static NSString *FieldString(const char *value)
{
    return value != NULL ? [NSString stringWithUTF8String:value] : nil;
}

static UIViewController *FieldTopViewController(void)
{
    UIWindow *window = nil;
    for (UIScene *scene in [UIApplication sharedApplication].connectedScenes)
    {
        if (![scene isKindOfClass:[UIWindowScene class]])
        {
            continue;
        }

        for (UIWindow *candidate in ((UIWindowScene *)scene).windows)
        {
            if (candidate.isKeyWindow)
            {
                window = candidate;
                break;
            }

            if (window == nil)
            {
                window = candidate;
            }
        }

        if (window != nil && window.isKeyWindow)
        {
            break;
        }
    }

    UIViewController *controller = window.rootViewController;
    while (controller.presentedViewController != nil)
    {
        controller = controller.presentedViewController;
    }

    return controller;
}

static void FieldSetAuthState(int state, NSError *error)
{
    s_fieldAuthState = state;
    s_fieldLastErrorCode = error != nil ? (long)error.code : 0;
    s_fieldAuthChanges++;
}

// ------------------------------------------------------------------ Game Center

void FieldGC_Authenticate(void)
{
    GKLocalPlayer *player = [GKLocalPlayer localPlayer];
    if (player.authenticateHandler != nil)
    {
        return; // already installed: GameKit calls it again on every change (e.g. after a sign-in in Settings)
    }

    FieldSetAuthState(player.isAuthenticated ? FieldGCStateAuthenticated : FieldGCStateAuthenticating, nil);
    player.authenticateHandler = ^(UIViewController *viewController, NSError *error)
    {
        dispatch_async(dispatch_get_main_queue(), ^{
            if (viewController != nil)
            {
                // Not signed in: keep GameKit's sign-in screen until the player asks for it (Settings > Sign in).
                s_fieldLoginController = viewController;
                FieldSetAuthState(FieldGCStateLoginAvailable, error);
            }
            else if ([GKLocalPlayer localPlayer].isAuthenticated)
            {
                s_fieldLoginController = nil;
                FieldSetAuthState(FieldGCStateAuthenticated, nil);
            }
            else
            {
                s_fieldLoginController = nil;
                FieldSetAuthState(FieldGCStateSignedOut, error);
            }
        });
    };
}

int FieldGC_AuthState(void)
{
    return s_fieldAuthState;
}

int FieldGC_AuthChangeCount(void)
{
    return s_fieldAuthChanges;
}

long long FieldGC_LastErrorCode(void)
{
    return (long long)s_fieldLastErrorCode;
}

int FieldGC_PresentLogin(void)
{
    if (s_fieldLoginController == nil)
    {
        return 0;
    }

    UIViewController *host = FieldTopViewController();
    if (host == nil)
    {
        return 0;
    }

    UIViewController *login = s_fieldLoginController;
    s_fieldLoginController = nil;
    FieldSetAuthState(FieldGCStateAuthenticating, nil);
    [host presentViewController:login animated:YES completion:nil];
    return 1;
}

char *FieldGC_PlayerName(void)
{
    GKLocalPlayer *player = [GKLocalPlayer localPlayer];
    return player.isAuthenticated ? FieldCopyString(player.displayName) : NULL;
}

char *FieldGC_PlayerId(void)
{
    GKLocalPlayer *player = [GKLocalPlayer localPlayer];
    return player.isAuthenticated ? FieldCopyString(player.teamPlayerID) : NULL;
}

void FieldGC_SubmitScore(const char *leaderboardId, long long score, long long context)
{
    NSString *identifier = FieldString(leaderboardId);
    GKLocalPlayer *player = [GKLocalPlayer localPlayer];
    if (identifier.length == 0 || !player.isAuthenticated)
    {
        return;
    }

    [GKLeaderboard submitScore:(NSInteger)score
                       context:(NSUInteger)(context > 0 ? context : 0)
                        player:player
                leaderboardIDs:@[ identifier ]
             completionHandler:^(NSError *error) {
                 if (error != nil)
                 {
                     NSLog(@"[BeamRunner] Game Center: score submission failed (%ld).", (long)error.code);
                 }
             }];
}

void FieldGC_ReportAchievement(const char *achievementId, int showBanner)
{
    NSString *identifier = FieldString(achievementId);
    if (identifier.length == 0 || ![GKLocalPlayer localPlayer].isAuthenticated)
    {
        return;
    }

    GKAchievement *achievement = [[GKAchievement alloc] initWithIdentifier:identifier];
    achievement.percentComplete = 100.0;
    achievement.showsCompletionBanner = showBanner != 0;
    [GKAchievement reportAchievements:@[ achievement ]
                withCompletionHandler:^(NSError *error) {
                    if (error != nil)
                    {
                        NSLog(@"[BeamRunner] Game Center: achievement report failed (%ld).", (long)error.code);
                    }
                }];
}

// state: 0 = leaderboards, 1 = achievements (GameKit's own GKGameCenterViewControllerState values).
int FieldGC_ShowDashboard(int state)
{
    if (![GKLocalPlayer localPlayer].isAuthenticated)
    {
        return 0;
    }

    GKGameCenterViewControllerState viewState =
        state == 1 ? GKGameCenterViewControllerStateAchievements : GKGameCenterViewControllerStateLeaderboards;
    [[GKAccessPoint shared] triggerAccessPointWithState:viewState handler:^{}];
    return 1;
}

// ------------------------------------------------------------------ iCloud key-value store

static void FieldKvsObserve(void)
{
    if (s_fieldKvsObserver != nil)
    {
        return;
    }

    s_fieldKvsObserver = [[NSNotificationCenter defaultCenter]
        addObserverForName:NSUbiquitousKeyValueStoreDidChangeExternallyNotification
                    object:[NSUbiquitousKeyValueStore defaultStore]
                     queue:[NSOperationQueue mainQueue]
                usingBlock:^(NSNotification *note) {
                    NSNumber *reason = note.userInfo[NSUbiquitousKeyValueStoreChangeReasonKey];
                    if (reason != nil && reason.integerValue == NSUbiquitousKeyValueStoreAccountChange)
                    {
                        s_fieldKvsAccountChanges++;
                    }

                    s_fieldKvsChanges++;
                }];
}

int FieldKvs_Synchronize(void)
{
    FieldKvsObserve();
    return [[NSUbiquitousKeyValueStore defaultStore] synchronize] ? 1 : 0;
}

char *FieldKvs_GetString(const char *key)
{
    NSString *name = FieldString(key);
    if (name.length == 0)
    {
        return NULL;
    }

    FieldKvsObserve();
    return FieldCopyString([[NSUbiquitousKeyValueStore defaultStore] stringForKey:name]);
}

int FieldKvs_SetString(const char *key, const char *value)
{
    NSString *name = FieldString(key);
    NSString *text = FieldString(value);
    if (name.length == 0 || text == nil)
    {
        return 0;
    }

    FieldKvsObserve();
    [[NSUbiquitousKeyValueStore defaultStore] setString:text forKey:name];
    return 1;
}

int FieldKvs_ChangeCount(void)
{
    return s_fieldKvsChanges;
}

int FieldKvs_AccountChangeCount(void)
{
    return s_fieldKvsAccountChanges;
}
