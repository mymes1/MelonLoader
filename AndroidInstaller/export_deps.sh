#!/bin/bash

set -e

BASE_DIR=$(realpath "$(dirname -- "$(readlink -f -- "${BASH_SOURCE}")")")
source "${BASE_DIR}/.env"
export MONO_BCL_DIR MONO_LIB_DIR BHAPTICS_LIB

bash "${BASE_DIR}/prepare-dependencies.sh"

OUTPUT_BASE="${BASE_DIR}/dependencies"
OUTPUT_ZIP="${BASE_DIR}/out/dependencies.zip"

mkdir -p "${BASE_DIR}/out"
rm -f "${OUTPUT_ZIP}"
(cd "${OUTPUT_BASE}" && zip -r "${OUTPUT_ZIP}" .)
