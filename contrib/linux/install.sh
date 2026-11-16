#!/bin/sh
# Installs ClevoControl on Linux: the desktop app, the clevoctl tool, permissions (udev + tmpfiles),
# the sleep hook, the systemd user service and a menu entry.
#
# Usage (from the extracted release folder):
#   sudo ./contrib/linux/install.sh               install
#   sudo ./contrib/linux/install.sh --with-input  also add you to the group "input" (reactive typing effect,
#                                                 scene hotkeys; lets you read keyboard events – see docs/LINUX.md)
#   sudo ./contrib/linux/install.sh --uninstall   remove
set -eu

here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)
user=${SUDO_USER:-}

if [ "$(id -u)" -ne 0 ]; then
  echo "Please run with sudo." >&2
  exit 1
fi

# Runs "systemctl --user ..." for the user who called sudo (needs their running session bus).
user_systemctl() {
  [ -n "$user" ] && [ "$user" != root ] || return 0
  uid=$(id -u "$user" 2>/dev/null) || return 0
  [ -S "/run/user/$uid/bus" ] || return 0
  if command -v runuser >/dev/null 2>&1; then
    runuser -u "$user" -- env XDG_RUNTIME_DIR="/run/user/$uid" DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/$uid/bus" \
      systemctl --user "$@" >/dev/null 2>&1 || true
  else
    sudo -u "$user" env XDG_RUNTIME_DIR="/run/user/$uid" DBUS_SESSION_BUS_ADDRESS="unix:path=/run/user/$uid/bus" \
      systemctl --user "$@" >/dev/null 2>&1 || true
  fi
}

if [ "${1:-}" = "--uninstall" ]; then
  # Stop and disable the user service before its unit file goes away.
  user_systemctl disable --now clevocontrol.service
  # "systemctl --user enable" links the unit into each user's default.target.wants: remove links left behind
  # (other users, or no running session for the invoking user).
  home=""
  [ -n "$user" ] && home=$(getent passwd "$user" | cut -d: -f6)
  for link in ${home:+"$home/.config/systemd/user/default.target.wants/clevocontrol.service"} \
              /home/*/.config/systemd/user/default.target.wants/clevocontrol.service \
              /root/.config/systemd/user/default.target.wants/clevocontrol.service; do
    if [ -L "$link" ]; then rm -f "$link"; fi
  done
  rm -f /usr/local/bin/clevoctl /usr/local/bin/clevocontrol \
        /etc/udev/rules.d/99-clevocontrol.rules /etc/tmpfiles.d/clevocontrol.conf \
        /usr/lib/systemd/system-sleep/clevocontrol /usr/lib/systemd/user/clevocontrol.service \
        /usr/share/applications/clevocontrol.desktop /usr/share/icons/hicolor/256x256/apps/clevocontrol.png
  udevadm control --reload-rules || true
  user_systemctl daemon-reload
  echo "Removed. Settings stay in ~/.config/ClevoControl; the group 'clevocontrol' was kept."
  exit 0
fi

find_bin() {
  for c in "$root/$1" "$here/$1" "$here/../$1"; do [ -f "$c" ] && { echo "$c"; return; }; done
}
ctl=$(find_bin clevoctl || true)
gui=$(find_bin clevocontrol || true)
if [ -z "$ctl" ] && [ -z "$gui" ]; then
  echo "Neither clevocontrol nor clevoctl found next to this script." >&2
  exit 1
fi

if [ ! -e /dev/tuxedo_io ]; then
  echo "warning: /dev/tuxedo_io does not exist. Install tuxedo-drivers first (see docs/LINUX.md)." >&2
fi
if systemctl is-enabled tccd >/dev/null 2>&1; then
  echo "warning: TUXEDO Control Center (tccd) is enabled and will fight with ClevoControl over fans and lighting." >&2
  echo "         Disable it with: sudo systemctl disable --now tccd tccd-sleep" >&2
fi

[ -n "$ctl" ] && install -m 0755 "$ctl" /usr/local/bin/clevoctl
[ -n "$gui" ] && install -m 0755 "$gui" /usr/local/bin/clevocontrol

getent group clevocontrol >/dev/null || groupadd --system clevocontrol
install -m 0644 "$here/99-clevocontrol.rules" /etc/udev/rules.d/99-clevocontrol.rules
install -m 0644 "$here/clevocontrol-tmpfiles.conf" /etc/tmpfiles.d/clevocontrol.conf
install -D -m 0755 "$here/clevocontrol-sleep" /usr/lib/systemd/system-sleep/clevocontrol
# The user service runs "clevoctl daemon": only useful when clevoctl is installed.
if [ -n "$ctl" ]; then
  install -D -m 0644 "$here/clevocontrol.service" /usr/lib/systemd/user/clevocontrol.service
fi
if [ -n "$gui" ]; then
  install -D -m 0644 "$here/clevocontrol.desktop" /usr/share/applications/clevocontrol.desktop
  install -D -m 0644 "$here/clevocontrol.png" /usr/share/icons/hicolor/256x256/apps/clevocontrol.png
  gtk-update-icon-cache -q /usr/share/icons/hicolor 2>/dev/null || true
fi

udevadm control --reload-rules
udevadm trigger --action=add --subsystem-match=leds --subsystem-match=power_supply || true
udevadm trigger --action=add --name-match=tuxedo_io || true
systemd-tmpfiles --create /etc/tmpfiles.d/clevocontrol.conf 2>/dev/null || true

if [ -n "$user" ] && [ "$user" != root ]; then
  usermod -aG clevocontrol "$user"
  echo "Added $user to the group 'clevocontrol'."
  if [ "${1:-}" = "--with-input" ]; then
    usermod -aG input "$user"
    echo "Added $user to the group 'input' (keyboard events for reactive typing and hotkeys)."
  fi
  echo "Log out and back in for the group membership to take effect."
fi

echo
echo "Installed. Next steps (as your normal user, after logging in again):"
[ -n "$ctl" ] && echo "  clevoctl status        # check that the hardware is detected"
[ -n "$gui" ] && echo "  clevocontrol           # start the app (also in your application menu)"
if [ -n "$ctl" ]; then
  echo "Without the desktop app, the background service runs lighting, fan curve and profiles:"
  echo "  systemctl --user enable --now clevocontrol"
fi
exit 0
