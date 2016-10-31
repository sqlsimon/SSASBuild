using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace SsasHelper
{
    /// <summary>
    /// Functions to help when manipulating XML documents.
    /// </summary>
    static class XmlHelper
    {
        /// <summary>
        /// Check to see if a node exists under a given node.
        /// </summary>
        /// <param name="parentNode">Parent node to check under</param>
        /// <param name="nodeToCheckFor">Name of the node to look for</param>
        /// <returns>true/false based on existence</returns>
        public static bool NodeExists(XmlNode parentNode, string nodeToCheckFor)
        {
            bool ret = false;

            foreach (XmlNode sibling in parentNode.ChildNodes)
            {
                if (sibling.Name == nodeToCheckFor)
                {
                    ret = true;
                    break;
                }
            }

            return ret;
        }

        /// <summary>
        /// Remove all nodes in the given XmlNodeList.
        /// </summary>
        /// <param name="nodeList">List of nodes to remove</param>
        /// <returns>Number of nodes removed</returns>
        public static int RemoveNodes(XmlNodeList nodeList)
        {
            int count = 0;

            foreach (XmlNode node in nodeList)
            {
                node.ParentNode.RemoveChild(node);
                ++count;
            }

            return count;
        }

        /// <summary>
        /// Remove the specified attribute from a node.
        /// </summary>
        /// <param name="nodeList">List of nodes to remove the attribute from</param>
        /// <param name="attributeName">Attribute to remove</param>
        /// <returns></returns>
        public static int RemoveAttributes(XmlNodeList nodeList, string attributeName)
        {
            int count = 0;

        	foreach(XmlElement node in nodeList)
            {
                node.RemoveAttribute(attributeName);
                ++count;
            }

            return count;
        }
    }
}
