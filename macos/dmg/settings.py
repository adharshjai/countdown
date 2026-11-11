# dmgbuild settings for Countdown's disk image: the app on the left, an Applications
# shortcut on the right, and a background with an arrow between them.
#     dmgbuild -s macos/dmg/settings.py -D app=... -D background=... -D icon=... Countdown out.dmg
import os.path

app = defines["app"]
app_name = os.path.basename(app)

format = "ULFO"            # LZFSE compression; opens on macOS 10.11 and later
filesystem = "HFS+"
size = None                # sized to fit

files = [app]
symlinks = {"Applications": "/Applications"}
icon = defines.get("icon")             # the volume's icon in Finder and on the desktop
background = defines["background"]     # a 640 x 400 point image, @1x and @2x

# Icon centres, matching macos/tools/make-dmg-background.swift.
icon_locations = {app_name: (170, 185), "Applications": (470, 185)}

window_rect = ((200, 140), (640, 400))
default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
show_icon_preview = False
include_icon_view_settings = True
include_list_view_settings = False

arrange_by = None
icon_size = 128
text_size = 13
label_pos = "bottom"
