﻿<?xml version="1.0" encoding="utf-8"?>
<!-- The original version of this file was contributed by Greg Galloway for the BidsHelper Project (http://bidshelper.codeplex.com/) -->
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:dwd="http://schemas.microsoft.com/DataWarehouse/Designer/1.0" xmlns:SSAS="http://schemas.microsoft.com/analysisservices/2003/engine" xmlns:msprop="urn:schemas-microsoft-com:xml-msprop" xmlns:xs="http://www.w3.org/2001/XMLSchema">
  <xsl:output cdata-section-elements="SSAS:Text"/>

  <xsl:template match="node()">
    <xsl:copy>
      <!-- Sort Measure Groups -->
      <xsl:apply-templates select="SSAS:MeasureGroup">
        <xsl:sort order="ascending" select="./SSAS:ID"/>
      </xsl:apply-templates>
      <!-- Sort Attributes by Name-->
      <xsl:apply-templates select="@*">
        <xsl:sort order="ascending" select="name()"/>
      </xsl:apply-templates>
      <!-- Leave default sort order for elements -->
      <xsl:apply-templates select="node()[local-name()!='Action' and name()!='xs:keyref' and local-name()!='MeasureGroup']" />
      <xsl:apply-templates select="./SSAS:Action">
        <xsl:sort order="ascending" select="./SSAS:ID"/>
      </xsl:apply-templates>
      <xsl:apply-templates select="./xs:keyref">
        <xsl:sort order="ascending" select="@name"/>
      </xsl:apply-templates>
    </xsl:copy>
  </xsl:template>

  <xsl:template match="@*">
    <xsl:attribute namespace="{namespace-uri()}" name="{name()}">
      <xsl:value-of select="."/>
    </xsl:attribute>
  </xsl:template>

  <xsl:template match="text()">
    <xsl:value-of select="."/>
  </xsl:template>

  <!--remove dwd:design-time-name attributes-->
  <xsl:template match="@dwd:design-time-name|@msprop:design-time-name"/>

  <!--remove any Microsoft Annotations -->
  <xsl:template match="//SSAS:Annotation[starts-with(SSAS:Name,'http://schemas.microsoft.com')]"/>

  <!--remove nodes which have meaningless dates or values -->
  <xsl:template match="//SSAS:CreatedTimestamp|//SSAS:LastSchemaUpdate|//SSAS:LastProcessed|//SSAS:State"/>
</xsl:stylesheet>