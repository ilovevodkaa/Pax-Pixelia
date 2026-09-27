# -*- coding: utf-8 -*-
"""Rebuild every concept sheet: python make_all.py  (Python 3 standard library only)"""
import os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import sheet_city, sheet_buildings, sheet_units, sheet_terrain, sheet_fog, sheet_flags, sheet_palette

for m in (sheet_city, sheet_buildings, sheet_units, sheet_terrain, sheet_fog, sheet_flags, sheet_palette):
    t = time.time()
    m.build()
    print('%-16s %.1fs' % (m.__name__, time.time() - t))
